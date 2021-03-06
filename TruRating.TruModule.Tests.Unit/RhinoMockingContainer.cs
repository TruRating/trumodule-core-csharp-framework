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
using System.Collections.Generic;
using System.Linq;
using Rhino.Mocks;

namespace TruRating.TruModule.Tests.Unit
{
    public class RhinoMockingContainer : IDisposable
    {
        private readonly IDictionary<string, object> _dictionary;

        public RhinoMockingContainer()
            : this(new Dictionary<string, object>())
        {
        }

        public RhinoMockingContainer(IDictionary<string, object> dictionary)
        {
            _dictionary = dictionary;
        }

        public void Dispose()
        {
            _dictionary.Clear();
        }

        public void Register<T>(T instance)
        {
            if (IsMocked<T>())
            {
                throw new ApplicationException("Already registered");
            }

            _dictionary.Add(typeof (T).FullName, instance);
        }

        public T Create<T>() where T : class
        {
            if (typeof (T).IsInterface) throw new ApplicationException("Can't create interface");

            if (IsMocked<T>())
            {
                return (T) _dictionary[typeof (T).FullName];
            }

            var constructorInfoObj = typeof (T).GetConstructors().OrderByDescending(x=> x.GetParameters().Length).FirstOrDefault(); //Get the constructor with the most arguments

            if (constructorInfoObj == null)
                throw new Exception("No public constructor");

            var parameters = constructorInfoObj.GetParameters();

            var mockedDependencies = new List<object>();
            foreach (var parameter in parameters)
            {
                mockedDependencies.Add(MockOf(parameter.ParameterType));
            }

            var instance = (T) constructorInfoObj.Invoke(mockedDependencies.ToArray());
            _dictionary.Add(typeof (T).FullName, instance);

            return instance;
        }

        private bool IsMocked(Type type)
        {
            return _dictionary.ContainsKey(type.FullName);
        }

        private bool IsMocked<T>()
        {
            return IsMocked(typeof (T));
        }

        public T MockOf<T>()
        {
            return (T) MockOf(typeof (T));
        }

        public object MockOf(Type type)
        {
            if (IsMocked(type))
            {
                return _dictionary[type.FullName];
            }

            _dictionary.Add(type.FullName, MockRepository.GenerateStub(type));

            return _dictionary[type.FullName];
        }
    }
}