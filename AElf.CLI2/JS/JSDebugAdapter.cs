﻿using AElf.Common.Attributes;
using ChakraCore.NET;
using ChakraCore.NET.Debug;
using NLog;

namespace AElf.CLI2.JS
{
    [LoggerName("js-debug")]
    public class JSDebugAdapter : IDebugAdapter
    {
        private ILogger _logger;

        public JSDebugAdapter(ILogger logger)
        {
            _logger = logger;
        }

        public void Init(IRuntimeDebuggingService debuggingService)
        {
            debuggingService.OnException += (sender, exception) =>
            {
                _logger.Fatal(
                    $"Javascript side raise an uncaught exception.\n${exception.ToString()}\n");
            };
        }
    }
}