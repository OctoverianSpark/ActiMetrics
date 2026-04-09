using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Text;
using ActiMetrics.Service.Services;

namespace ActiMetrics.Service.Workers
{
    public class WebSocketWorker : BackgroundService
    {

        private readonly WebSocketService _webSocketService;


        public WebSocketWorker(WebSocketService webSocketService)
        {
            _webSocketService = webSocketService;
        }


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await _webSocketService.StartAsync(stoppingToken);

        }
    }
}
