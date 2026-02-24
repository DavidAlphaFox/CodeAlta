// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System.Text.Json;
using CodeNoesis.CodexSdk;

namespace CodeNoesis.Tests;

[TestClass]
public class JsonRpcExceptionTests
{
    [TestMethod]
    public void Constructor_SetsProperties()
    {
        var data = JsonDocument.Parse("""{"detail":"extra"}""").RootElement;
        var ex = new JsonRpcException(-32001, "Server overloaded", data);

        Assert.AreEqual(-32001, ex.Code);
        Assert.AreEqual("Server overloaded", ex.Message);
        Assert.IsNotNull(ex.Data);
    }

    [TestMethod]
    public void IsRetryable_TrueForOverloadCode()
    {
        var ex = new JsonRpcException(-32001, "Server overloaded");
        Assert.IsTrue(ex.IsRetryable);
    }

    [TestMethod]
    public void IsRetryable_FalseForOtherCodes()
    {
        var ex = new JsonRpcException(-32600, "Invalid request");
        Assert.IsFalse(ex.IsRetryable);
    }
}
