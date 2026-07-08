
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
        private string _lastTooltip = "Go Tracer";

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
                Text = "Go Tracer",
                ContextMenuStrip = _menu
            };

            _timerService.SetTrayService(this);
            _websocketService.OnNotification += OnNotification;
            _syncService.StateCatalogRefreshed += OnStateCatalogRefreshed;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }

        private void OnStateCatalogRefreshed()
        {
            _uiContext.Post(_ => RebuildStateItems(), null);
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
        private const int FixedTrailingMenuItems = 2; // separador + "Crear Ticket"
        private WorkState _lastActiveState = WorkState.Working;

        private static readonly Dictionary<string, string> _categoryEmojis = new(StringComparer.OrdinalIgnoreCase)
        {
            ["active"] = "🟢",
            ["neutral"] = "🟢",
            ["inactive"] = "🔴",
        };

        private ContextMenuStrip BuildMenu()
        {
            var menu = new ContextMenuStrip();
            PopulateStateItems(menu);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Crear Ticket", null, (s, e) => OpenTicketForm()));

            UpdateMenuCheck(_lastActiveState);
            return menu;
        }

        private void RebuildStateItems()
        {
            while (_menu.Items.Count > FixedTrailingMenuItems)
            {
                var item = _menu.Items[0];
                _menu.Items.RemoveAt(0);
                item.Dispose();
            }

            _stateItems.Clear();
            PopulateStateItems(_menu, insertAt: 0);
            UpdateMenuCheck(_lastActiveState);
        }

        private void PopulateStateItems(ContextMenuStrip menu, int insertAt = -1)
        {
            var states = _syncService.States.Where(s => s.Show_In_Menu).ToList();
            var groups = states.Count > 0
                ? BuildCatalogGroups(states)
                : BuildFallbackGroups();

            var index = insertAt;
            foreach (var categoryItem in groups)
            {
                if (index < 0) menu.Items.Add(categoryItem);
                else menu.Items.Insert(index++, categoryItem);
            }
        }

        private List<ToolStripMenuItem> BuildCatalogGroups(List<StateCatalogItem> visibleStates)
        {
            var categoriesById = _syncService.StateCategories.ToDictionary(c => c.Id);
            var ordered = visibleStates
                .OrderBy(s => categoriesById.TryGetValue(s.Category_Id, out var c) ? c.Sort_Order : int.MaxValue)
                .ThenBy(s => s.Sort_Order);

            var result = new List<ToolStripMenuItem>();
            foreach (var group in ordered.GroupBy(s => s.Category_Id))
            {
                categoriesById.TryGetValue(group.Key, out var categoryInfo);
                var categoryItem = new ToolStripMenuItem(FormatCategoryLabel(categoryInfo));

                foreach (var stateItem in group)
                {
                    if (!Enum.IsDefined(typeof(WorkState), stateItem.Code))
                        continue; // estado sin equivalente local soportado aún

                    var capturedState = (WorkState)stateItem.Code;
                    var item = new ToolStripMenuItem(stateItem.Name);
                    item.Click += async (s, e) =>
                    {
                        await _timerService.SetStateAsync(capturedState);
                        UpdateMenuCheck(capturedState);
                    };
                    _stateItems[capturedState] = item;
                    categoryItem.DropDownItems.Add(item);
                }

                if (categoryItem.DropDownItems.Count > 0)
                    result.Add(categoryItem);
                else
                    categoryItem.Dispose();
            }

            return result;
        }

        private static string FormatCategoryLabel(StateCategoryCatalogItem? category)
        {
            if (category is null) return "Otros";
            var emoji = _categoryEmojis.TryGetValue(category.Key, out var e) ? e + " " : string.Empty;
            return emoji + category.Name;
        }

        private List<ToolStripMenuItem> BuildFallbackGroups()
        {
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

            var result = new List<ToolStripMenuItem>();
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
                result.Add(categoryItem);
            }

            return result;
        }

        public void OpenTicketForm()
        {
            var ticketForm = new Ticket(_syncService);
            ticketForm.Show();
        }

        public void UpdateMenuCheck(WorkState activeState)
        {
            _lastActiveState = activeState;
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
            _syncService.StateCatalogRefreshed -= OnStateCatalogRefreshed;
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _menu.Dispose();
        }
    }
}
