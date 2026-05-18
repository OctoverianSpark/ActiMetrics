
using System.Drawing;
using System.Windows.Forms;
using ActiMetrics.Service.Services;
using ActiMetrics.Shared;
using ActiMetrics.Shared.Extensions;
using ActiMetrics.Shared.Models;
using ActiMetrics.UI.Tray;
using Microsoft.Win32;

namespace ActiMetrics.UI.Tray
{
    public class TrayService : ITrayService, IDisposable
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly TimerService _timerService;
        private readonly WebSocketService _websocketService;
        private readonly SyncService _syncService;
        private readonly SynchronizationContext _uiContext;
        private readonly ContextMenuStrip _menu;
        private string _lastTooltip = "Tracer";

        public TrayService(TimerService timerService, WebSocketService socket, SyncService syncService, SynchronizationContext uiContext)
        {
            _timerService = timerService;
            _syncService = syncService;
            _websocketService = socket;
            _uiContext = uiContext;

            _menu = BuildMenu();

            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            _notifyIcon = new NotifyIcon
            {
                Icon = File.Exists(iconPath) ? new Icon(iconPath) : SystemIcons.Application,
                Visible = true,
                Text = "Tracer",
                ContextMenuStrip = _menu
            };

            _timerService.SetTrayService(this);
            _websocketService.OnNotification += OnNotification;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }

        private void OnNotification((string Title, string Text) tuple)
        {
            Console.WriteLine($"[Toast] Recibido: {tuple.Title} - {tuple.Text}");

            _uiContext.Post(_ =>
            {
                Console.WriteLine("[Toast] Mostrando...");
                ToastNotification notification = new(tuple.Title, tuple.Text);
                notification.Show();
            }, null);
        }

        public void Notify((string Title, string Text) tuple)
        {
            _uiContext.Post(_ =>
            {
                ToastNotification notification = new(tuple.Title, tuple.Text);
                notification.Show();
            }, null);
        }

        public void NotifyWithAction((string Title, string Text) tuple, string actionLabel, Action onAction)
        {
            _uiContext.Post(_ =>
            {
                ToastNotification notification = new(tuple.Title, tuple.Text, 15000, actionLabel, onAction);
                notification.Show();
            }, null);
        }

        private readonly Dictionary<WorkState, ToolStripMenuItem> _stateItems = new();

        private ContextMenuStrip BuildMenu()
        {
            var menu = new ContextMenuStrip();
            var categoryLabels = new Dictionary<StateCategory, string>
            {
                { StateCategory.Active,   "🟢 Activo"   },
                { StateCategory.Neutral,  "🟢 Neutral"  },
                { StateCategory.Inactive, "🔴 Inactivo" },
            };

            var grouped = Enum.GetValues<WorkState>()
                .Where(s => s is not WorkState.Idle and not WorkState.Offline)
                .Select(state => (state, info: state.GetInfo()))
                .Where(x => x.info is not null)
                .GroupBy(x => x.info!.Category);

            foreach (var group in grouped)
            {
                var categoryItem = new ToolStripMenuItem(categoryLabels[group.Key]);
                foreach (var (state, info) in group)
                {
                    var capturedState = state;
                    var item = new ToolStripMenuItem(info!.Label);
                    item.Click += async (s, e) =>
                    {
                        await _timerService.SetStateAsync(capturedState);
                        UpdateMenuCheck(capturedState);
                    };
                    _stateItems[state] = item;
                    categoryItem.DropDownItems.Add(item);
                }
                menu.Items.Add(categoryItem);
            }

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Crear Ticket", null, (s, e) => OpenTicketForm()));

            UpdateMenuCheck(WorkState.Working);
            return menu;
        }

        public void OpenTicketForm()
        {
            var ticketForm = new Ticket(_syncService);
            ticketForm.Show();
        }

        public void UpdateMenuCheck(WorkState activeState)
        {
            foreach (var (state, item) in _stateItems)
                item.Checked = state == activeState;
        }

        public void UpdateTooltip(string text)
        {
            var truncated = text.Length > 63 ? text[..63] : text;
            _lastTooltip = truncated;
            _uiContext.Post(_ =>
            {
                _notifyIcon.Text = truncated;
            }, null);
        }

        private async void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode != PowerModes.Resume) return;
            await Task.Delay(2000);
            _uiContext.Post(_ => _notifyIcon.Text = _lastTooltip, null);
        }

        public void Dispose()
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            _websocketService.OnNotification -= OnNotification;
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _menu.Dispose();
        }
    }
}
