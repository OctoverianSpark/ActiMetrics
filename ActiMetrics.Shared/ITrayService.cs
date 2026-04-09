using System;
using System.Collections.Generic;
using System.Text;

namespace ActiMetrics.Shared
{
    public interface ITrayService
    {
        void UpdateTooltip(string text);

        void Notify((string Title, string Text) tuple);
    }
}
