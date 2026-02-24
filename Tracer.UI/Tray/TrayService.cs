using System.Drawing;
using System.Windows.Forms;
using Tracer.Service.Services;
using Tracer.Shared.Models;
using Tracer.Shared;
using Tracer.UI.Tray;
using Tracer.Shared.Extensions;


namespace Tracer.UI.Tray
{
    public class TrayService : ITrayService , IDisposable
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly TimerService _timerService;

        public TrayService(TimerService timerService)
        {
            _timerService = timerService;

            _notifyIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Visible = true,
                Text = "Tracer"
            };

            _notifyIcon.ContextMenuStrip = BuildMenu();
            _timerService.SetTrayService(this);
        }
        private readonly Dictionary<WorkState, ToolStripMenuItem> _stateItems = new();

        private ContextMenuStrip BuildMenu()
        {
            var menu = new ContextMenuStrip();

            var categoryLabels = new Dictionary<StateCategory, string>
    {
        { StateCategory.Active,   "🟢 Activo"   },
        { StateCategory.Neutral,   "🟢 Neutral"   },
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
                        UpdateMenuCheck(capturedState); // ← actualiza el check
                    };

                    _stateItems[state] = item; // ← guarda referencia al item
                    categoryItem.DropDownItems.Add(item);
                }

                menu.Items.Add(categoryItem);
            }

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("❌ Salir", null, (s, e) => Application.Exit());

            // Check inicial
            UpdateMenuCheck(WorkState.Working);

            return menu;
        }

        public void UpdateMenuCheck(WorkState activeState)
        {
            foreach (var (state, item) in _stateItems)
            {
                item.Checked = state == activeState;
            }
        }
        public void UpdateTooltip(string text)
        {
            _notifyIcon.Text = text.Length > 63 ? text[..63] : text;
        }

        public void Dispose()
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
    }
}
