﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using SmartFormat.Extensions;

namespace SmartFormat.Tests
{
    [TestFixture]
    public class DictionaryFormatterTests
    {
        public object[] GetArgs()
        {
            var d = new Dictionary<string, object>() {
                {"Numbers", new Dictionary<string, object>() {
                    {"One", 1},
                    {"Two", 2},
                    {"Three", 3},
                }},
                {"Letters", new Dictionary<string, object>() {
                    {"A", "a"},
                    {"B", "b"},
                    {"C", "c"},
                }},
                {"Object", new {
                    Prop1 = "a",
                    Prop2 = "b",
                    Prop3 = "c",
                }},
            };

            return new object[] {
                d,
            };
        }

        [Test]
        public void Test_Dictionary()
        {
            var formatter = Smart.CreateDefaultSmartFormat();
            formatter.AddExtensions(new DictionarySource(formatter));

            var formats = new string[] {
                "Chained: {0.Numbers.One} {Numbers.Two} {Letters.A} {Object.Prop1} ",
                "Nested: {0:{Numbers:{One} {Two}} } {Letters:{A}} {Object:{Prop1}} ", // Due to double-brace escaping, the spacing in this nested format is irregular
            };
            var expected = new string[] {
                "Chained: 1 2 a a ",
                "Nested: 1 2  a a ",
            };
            var args = GetArgs();
            formatter.Test(formats, args, expected);
        }

    }
}
