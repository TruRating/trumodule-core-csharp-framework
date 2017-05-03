﻿// The MIT License
// 
// Copyright (c) 2017 TruRating Ltd. https://www.trurating.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
using System;
using System.Diagnostics;
using System.Threading;
using TruRating.Dto.TruService.V220;
using TruRating.TruModule.Device;
using TruRating.TruModule.Messages;
using TruRating.TruModule.Network;
using TruRating.TruModule.Settings;
using TruRating.TruModule.Util;

namespace TruRating.TruModule
{
    public abstract class TruModule
    {
        private readonly AutoResetEvent _dwellTimeExtendAutoResetEvent = new AutoResetEvent(false);
        protected readonly ILogger Logger;
        private readonly ITruServiceClient _truServiceClient;
        protected readonly IDevice Device;
        protected readonly IReceiptManager ReceiptManager;
        protected readonly ISettings Settings;
        protected readonly ITruServiceMessageFactory TruServiceMessageFactory;
        private int _dwellTimeExtendMs;
        /// <summary>
        /// Used to indicate question was programatically cancelled.
        /// </summary>
        private volatile bool _isCancelled;
        private volatile bool _isQuestionRunning;
        protected internal DateTime ActivationRecheck;
        protected internal bool Activated;

        protected TruModule(IDevice device, IReceiptManager receiptManager, ITruServiceClient truServiceClient, ILogger logger,
            ITruServiceMessageFactory truServiceMessageFactory, ISettings settings)
        {
            Device = device;
            _truServiceClient = truServiceClient;
            Logger = logger;
            TruServiceMessageFactory = truServiceMessageFactory;
            Settings = settings;
            ReceiptManager = receiptManager;
            SessionId = DateTimeProvider.UtcNow.Ticks.ToString();
            IsActivated(bypassTruServiceCache: true);
        }

        protected string SessionId { get; set; }

        public virtual bool IsActivated(bool bypassTruServiceCache) //TODO - comment ont his
        {
            try
            {
                if (ActivationRecheck > DateTimeProvider.UtcNow && !bypassTruServiceCache)
                {
                    Logger.Debug("Not querying TruService status, next check at {0}. IsActive is {1}",
                        ActivationRecheck, Activated);
                    return Activated;
                }
                var status =
                    _truServiceClient.Send(TruServiceMessageFactory.AssemblyRequestQuery(new RequestParams(Settings, SessionId), Device, ReceiptManager, bypassTruServiceCache));

                var responseStatus = status != null ? status.Item as ResponseStatus : null;
                if (responseStatus != null)
                {
                    ActivationRecheck = DateTimeProvider.UtcNow.AddSeconds(responseStatus.TimeToLive);
                    Activated = responseStatus.IsActive;
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, "Error in IsActivated");
            }
            return Activated;
        }

        public void CancelRating()
        {
            try
            {
                _isCancelled = true; //Set flag to show question has been cancelled
                if (_isQuestionRunning)
                {
                    if (Settings.Trigger == Trigger.DWELLTIMEEXTEND)
                    {
                        Logger.Debug("Waiting {0} to cancel rating", _dwellTimeExtendMs);
                        _dwellTimeExtendAutoResetEvent.WaitOne(_dwellTimeExtendMs); //Wait for dwelltime extend to finish
                        if (_isQuestionRunning) //recheck _isQuestionRunning because customer may have provided rating
                        {
                            Device.ResetDisplay(); //Force the 1AQ1KR loop to exit and release control of the PED
                        }
                    }
                    else
                    {
                        Device.ResetDisplay(); //Force the 1AQ1KR loop to exit and release control of the PED
                    }
                    Logger.Debug("Cancelled rating");
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, "Error in CancelRating");
            }
        }

        protected Response SendRequest(Request request)
        {
            return _truServiceClient.Send(request);
        }

        protected void DoRating(Request request)
        {
            try
            {
                _isCancelled = false;
                if (!(request.Item is RequestQuestion))
                {
                    Logger.Info("Request was not a question");
                    return;
                }
                Settings.Trigger = ((RequestQuestion)request.Item).Trigger;
                var response = _truServiceClient.Send(request);
                if (response == null)
                {
                    Logger.Info("Response was null");
                    return;
                }
                ResponseQuestion question;
                ResponseReceipt[] receipts;
                ResponseScreen[] screens;
                var rating = new RequestRating //Have a question, construct the basic rating record
                {
                    DateTime = DateTimeProvider.UtcNow,
                    Rfc1766 = Device.GetCurrentLanguage(),
                    Value = -4 //initial state is "cannot show question"
                };
                //Look through the response for a valid question
                ResponseScreen responseScreen = null;
                var whenToDisplay = rating.Value < 0 ? When.NOTRATED : When.RATED;
                var hasRated = whenToDisplay == When.RATED;
                if (TruModuleHelpers.QuestionAvailable(response, rating.Rfc1766, out question, out receipts, out screens))
                {
                    var sw = new Stopwatch();
                    sw.Start();
                    var trigger = ((RequestQuestion)request.Item).Trigger; //Grab the trigger from the question request
                    var timeoutMs = question.TimeoutMs;
                    if (trigger == Trigger.DWELLTIMEEXTEND) //TODO comments
                    {
                        timeoutMs = int.MaxValue;
                        _dwellTimeExtendMs = question.TimeoutMs;
                    }
                    _isQuestionRunning = true;
                    rating.Value = Device.Display1AQ1KR(question.Value, timeoutMs);
                    //Wait for the user input for the specified period
                    _isQuestionRunning = false;
                    _dwellTimeExtendAutoResetEvent.Set();
                    //Signal to CancelRating that question has been answered when called in another thread.
                    sw.Stop();
                    rating.ResponseTimeMs = (int)sw.ElapsedMilliseconds; //Set the response time
                    var responseReceipt = TruModuleHelpers.GetResponseReceipt(receipts, whenToDisplay);
                    responseScreen = TruModuleHelpers.GetResponseScreen(screens, whenToDisplay);
                    ReceiptManager.AppendReceipt(responseReceipt);
                }
                _truServiceClient.Send(TruServiceMessageFactory.AssembleRequestRating(request, rating));//TODO wrap in task.
                if (responseScreen != null && (!_isCancelled || responseScreen.Priority))
                {
                    var messageContext = responseScreen.Priority && hasRated ? MessageContext.PRIZE : MessageContext.NONE;
                    Device.DisplayAcknowledgement(responseScreen.Value, responseScreen.TimeoutMs, hasRated, messageContext); //TODO
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, "Error in DoRating");
            }
        }

    }
}