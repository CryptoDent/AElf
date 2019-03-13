﻿using System.Collections.Generic;
using Xunit;
using AElf.Common;
using Google.Protobuf;
using Shouldly;

namespace AElf.Types.Tests.Extensions
{
    public class ExtensionTests
    {
        [Fact]
        public void String_Extension_Methods()
        {
            var hexValue = Hash.Generate().ToHex();

            var hexValueWithPrefix = hexValue.AppendHexPrefix();
            hexValueWithPrefix.Substring(0, 2).ShouldBe("0x");
            var hexValueWithPrefix1 = hexValueWithPrefix.AppendHexPrefix();
            hexValueWithPrefix1.ShouldBeSameAs(hexValueWithPrefix);

            var hex = hexValueWithPrefix.RemoveHexPrefix();
            hex.ShouldBe(hexValue);
            var hex1 = hex.RemoveHexPrefix();
            hex1.ShouldBeSameAs(hex);

            var hash1 = hexValue.CalculateHash();
            hash1.ShouldNotBe(null);
        }

        [Fact]
        public void Numberc_Extensions_Methods()
        {
            //ulong
            var uNumber = (ulong)10;
            var byteArray = uNumber.ToBytes();
            byteArray.ShouldNotBe(null);

            //int
            var iNumber = 10;
            var byteArray1 = iNumber.DumpByteArray();
            byteArray1.ShouldNotBe(null);

            //hash
            var hash = iNumber.ComputeHash();
            hash.ShouldNotBe(null);
        }
    }
}