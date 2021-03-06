// The MIT License
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
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rhino.Mocks;
using TruRating.Dto.TruService.V220;
using TruRating.TruModule.Network;
using TruRating.TruModule.Settings;
using TruRating.TruModule.Util;

namespace TruRating.TruModule.Tests.Unit.TruModuleTests.Activation
{
    [TestClass]
    public class WhenTruModuleIsInactiveAndTtlNotExpiredButForcingAnActivationCheck : TruModuleTestsContext
    {
        private ITruServiceClient _truServiceClient;
        private ISettings _settings;
        private bool _result;

        [TestInitialize]
        public void Setup()
        {
            DateTimeProvider.UtcNow = new DateTime(2001, 01, 01, 11, 00,00);
            _settings = MockOf<ISettings>();
            Sut.ActivationRecheck = new DateTime(2001, 01, 01,12,00,00);
            _truServiceClient = MockOf<ITruServiceClient>();
            _truServiceClient.Stub(x => x.Send(Arg<Request>.Is.Anything))
                .Return(new Response() {Item = new ResponseStatus() {TimeToLive = 36000, IsActive = true}});
            _result = Sut.IsActivated(bypassTruServiceCache:true);
        }

        [TestMethod]
        public void ItShouldBeActive()
        {
            Assert.IsTrue(_result);
        }

        [TestMethod]
        public void ItShouldAdvanceTheTTL()
        {
            Assert.IsTrue(Sut.ActivationRecheck == new DateTime(2001, 01, 01, 21, 00, 00));
        }

        [TestMethod]
        public void ItShouldHaveMadeTwoCallsToTruService()
        {
            _truServiceClient.AssertWasCalled(x => x.Send(Arg<Request>.Is.Anything), options => options.Repeat.Twice());
        }
    }
}