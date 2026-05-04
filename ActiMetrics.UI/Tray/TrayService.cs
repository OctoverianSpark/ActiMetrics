
using System.Drawing;
using System.Windows.Forms;
using ActiMetrics.Service.Services;
using ActiMetrics.Shared;
using ActiMetrics.Shared.Extensions;
using ActiMetrics.Shared.Models;
using ActiMetrics.UI.Tray;

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
            _notifyIcon.Text = text.Length > 63 ? text[..63] : text;
        }

        public void Dispose()
        {
            _websocketService.OnNotification -= OnNotification;
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _menu.Dispose();
        }
    }
}