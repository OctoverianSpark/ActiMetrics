using System.Diagnostics;
using System.Runtime.InteropServices;
using ActiMetrics.Data;
using ActiMetrics.Shared;
using ActiMetrics.Shared.Models;

namespace ActiMetrics.Service.Services
{
    public class TimerService
    {
        private bool _isLate = false;
        private bool _isReady = false;
        private readonly StateRepository _repository;
        private readonly ActivityService _activityService;
        private readonly SyncService _syncService;
        private readonly string _workerId;
        private readonly string _workerUserName;
        private ITrayService? _trayService;
        private StateCategory _currentCategory = StateCategory.Inactive;
        private WorkState _currentState = WorkState.Idle;
        private StateType _currentType = StateType.Auto;
        private Programation? _todayProgramation;

        public bool IsReady => _isReady;

        [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode)]
        private static extern bool WTSQuerySessionInformation(IntPtr hServer, int sessionId, int wtsInfoClass, out IntPtr ppBuffer, out uint pBytesReturned);

        [DllImport("wtsapi32.dll")]
        private static extern void WTSFreeMemory(IntPtr pMemory);

        [DllImport("kernel32.dll")]
        private static extern uint WTSGetActiveConsoleSessionId();

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        private static bool IsRemoteSession() => GetSystemMetrics(0x1000) != 0; // SM_REMOTESESSION

        private static string GetActualUserName()
        {
            try
            {
                var sessionId = (int)WTSGetActiveConsoleSessionId();
                if (WTSQuerySessionInformation(IntPtr.Zero, sessionId, 5 /* WTSUserName */, out var buffer, out _))
                {
                    try { return Marshal.PtrToStringUni(buffer) ?? Environment.UserName; }
                    finally { WTSFreeMemory(buffer); }
                }
            }
            catch { }
            return Environment.UserName;
        }

        public void SetTrayService(ITrayService trayService)
        {
            _trayService = trayService;
        }

        public TimerService(StateRepository repository, ActivityService activityService, SyncService syncService)
        {
            _repository = repository;
            _activityService = activityService;
            _syncService = syncService;
            _workerId = Environment.MachineName;
            _workerUserName = GetActualUserName();
        }

        public async Task FallbackInitializeAsync()
        {
            await LogStateAsync(StateCategory.Active, WorkState.Working, StateType.Auto);
            _isReady = true;
            Console.WriteLine($"[Tracer] Iniciado en modo libre (sin conexión) | Worker: {_workerId}");
        }

        public async Task InitializeAsync()
        {
            _todayProgramation = await _syncService.GetTodayScheduleAsync().ConfigureAwait(false);

            if (_todayProgramation is null)
            {
                Console.WriteLine($"[Tracer] Sin programación para hoy. Iniciando modo libre de conteo de horas. Worker: {_workerId}");
                // Modo libre: iniciar como Working sin restricciones de horario
                await LogStateAsync(StateCategory.Active, WorkState.Working, StateType.Auto);
                _isReady = true;
                Console.WriteLine($"[Tracer] Iniciado en modo libre | Worker: {_workerId}");
                return;
            }

            var (state, category, type, isLate) = ResolveInitialState(_todayProgramation);
            await LogStateAsync(category, state, type);

            _isLate = isLate;
            _isReady = true;

            if (_isLate)
                Console.WriteLine($"[Tracer] ⚠ Entrada tardía detectada | Worker: {_workerId}");

            Console.WriteLine($"[Tracer] Iniciado | Worker: {_workerId}");
        }

        private (WorkState state, StateCategory category, StateType type, bool isLate) ResolveInitialState(Programation programation)
        {
            var now = TimeOnly.FromDateTime(DateTime.Now);
            var startDay = TimeOnly.ParseExact(programation.Start_Day, "HH:mm", null);
            var gracePeriod = startDay.AddMinutes(5);
            bool isLate = now > gracePeriod;
            return (WorkState.Working, StateCategory.Active, StateType.Auto, isLate);
        }

        private async Task HandleStateTransition(bool isIdle)
        {
            var newState = (_currentCategory, _currentState, _currentType, isIdle) switch
            {
                (StateCategory.Active, _, _, true)
                    => (StateCategory.Inactive, WorkState.Idle, StateType.Auto),

                (_, WorkState.Idle, StateType.Auto, false)
                    => (StateCategory.Active, WorkState.Working, StateType.Auto),

                (_, _, StateType.Manual, _)
                    => ((StateCategory?)null, (WorkState?)null, (StateType?)null),

                _ => ((StateCategory?)null, (WorkState?)null, (StateType?)null)
            };

            var (category, state, type) = newState;
            if (category is not null)
                await LogStateAsync(category.Value, state!.Value, type!.Value);
        }
        private bool _endDaySoonNotified = false;
        private bool _endDayReachedNotified = false;
        private bool _shutdownFiveMinNotified = false;
        private bool _shutdownMinuteNotified = false;
        private bool _isOvertimeConfirmed = false;
        private DateTime? _shutdownAt = null;

        private bool _lunchSoonNotified = false;
        private Task CheckLunchSoonWarningAsync()
        {
            if (_todayProgramation is null) return Task.CompletedTask;

            var ahora = TimeOnly.FromDateTime(DateTime.Now);
            var startLunch = TimeOnly.ParseExact(_todayProgramation.Start_Lunch, "HH:mm", null);
            var aviso = startLunch.AddMinutes(-5);

            if (ahora < aviso || ahora >= startLunch)
            {
                _lunchSoonNotified = false;
                return Task.CompletedTask;
            }

            if (!_lunchSoonNotified)
            {
                _lunchSoonNotified = true;
                _trayService?.Notify(("Almuerzo próximo", $"Tu almuerzo comienza en 5 minutos ({startLunch:HH:mm})."));
                Console.WriteLine($"[Tracer] Almuerzo en 5 minutos ({startLunch:HH:mm})");
            }

            return Task.CompletedTask;
        }

        private async Task CheckEndDayAsync()
        {
            if (_todayProgramation is null || string.IsNullOrEmpty(_todayProgramation.End_Day)) return;

            if (_currentState == WorkState.Overtime || _currentState == WorkState.Offline)
            {
                _shutdownAt = null;
                _shutdownFiveMinNotified = false;
                _shutdownMinuteNotified  = false;
                return;
            }

            var ahora = TimeOnly.FromDateTime(DateTime.Now);
            var endDay = TimeOnly.ParseExact(_todayProgramation.End_Day, "HH:mm", null);
            var aviso = endDay.AddMinutes(-5);

            if (ahora >= aviso && ahora < endDay && !_endDaySoonNotified)
            {
                _endDaySoonNotified = true;
                _trayService?.Notify(("⚠️ Fin de jornada próximo",
                    $"Tu jornada termina a las {_todayProgramation.End_Day}. Selecciona 'Horas Extras' en el menú si vas a quedarte."));
                Console.WriteLine($"[Tracer] Aviso fin de jornada: {_todayProgramation.End_Day}");
            }

            if (ahora >= endDay && !_endDayReachedNotified)
            {
                _endDayReachedNotified = true;
                if (IsRemoteSession())
                {
                    _trayService?.Notify(("🔴 Jornada finalizada", "Sesión remota detectada — el equipo no se apagará automáticamente."));
                    Console.WriteLine("[Tracer] Jornada finalizada (sesión RDP — apagado automático desactivado).");
                }
                else
                {
                    _shutdownAt = DateTime.Now.AddMinutes(60);
                    _trayService?.NotifyWithAction(
                        ("🔴 Jornada finalizada", "El equipo se apagará en 1 hora. Selecciona 'Horas Extras' en el menú para cancelar."),
                        "30 minutos más",
                        () => { _shutdownAt = DateTime.Now.AddMinutes(30); _shutdownFiveMinNotified = false; _shutdownMinuteNotified = false; });
                    Console.WriteLine("[Tracer] Jornada finalizada. Apagado programado en 60 min.");
                }
            }

            if (_shutdownAt is null) return;

            var restante = _shutdownAt.Value - DateTime.Now;

            if (restante.TotalMinutes <= 5 && restante.TotalSeconds > 0 && !_shutdownFiveMinNotified)
            {
                _shutdownFiveMinNotified = true;
                _trayService?.NotifyWithAction(
                    ("⏻ Apagado próximo", "El equipo se apagará en 5 minutos. Última oportunidad para marcar 'Horas Extras'."),
                    "30 minutos más",
                    () => { _shutdownAt = DateTime.Now.AddMinutes(30); _shutdownFiveMinNotified = false; _shutdownMinuteNotified = false; });
                Console.WriteLine("[Tracer] Apagado en 5 minutos.");
            }

            if (restante.TotalMinutes <= 1 && restante.TotalSeconds > 0 && !_shutdownMinuteNotified)
            {
                _shutdownMinuteNotified = true;
                _trayService?.NotifyWithAction(
                    ("⏻ Apagado inminente", "El equipo se apagará en 1 minuto. Última oportunidad para marcar 'Horas Extras'."),
                    "30 minutos más",
                    () => { _shutdownAt = DateTime.Now.AddMinutes(30); _shutdownFiveMinNotified = false; _shutdownMinuteNotified = false; });
                Console.WriteLine("[Tracer] Apagado en 1 minuto.");
            }

            if (DateTime.Now >= _shutdownAt.Value)
            {
                Console.WriteLine("[Tracer] Iniciando apagado del sistema.");
                _shutdownAt = null;
                await LogStateAsync(StateCategory.Inactive, WorkState.Offline, StateType.Auto);
                try
                {
                    Process.Start(new ProcessStartInfo("shutdown", "/s /t 60 /c \"ActiMetrics: Jornada laboral finalizada.\"")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Tracer] Error al iniciar apagado: {ex.Message}");
                }
            }
        }

        private bool _lunchStartNotified = false;
        private Task CheckLunchStartWarningAsync()
        {
            if (_todayProgramation is null) return Task.CompletedTask;

            if (_currentState != WorkState.Lunch)
            {
                _lunchStartNotified = false;
                return Task.CompletedTask;
            }

            if (!_lunchStartNotified)
            {
                _lunchStartNotified = true;
                var endLunch = TimeOnly.ParseExact(_todayProgramation.End_Lunch, "HH:mm", null);
                _trayService?.Notify(
                    (
                    "🍽️ Hora de almorzar",
                    $"Recuerda volver a las {endLunch:HH:mm}.")
                );
                Console.WriteLine($"[Tracer] 🍽️ Almuerzo iniciado. Regreso a las {endLunch:HH:mm}");
            }

            return Task.CompletedTask;
        }
        public async Task Tick()
        {
            if (!_isReady) return;

            bool isIdle = _activityService.IsIdle();
            await HandleStateTransition(isIdle);
            await CheckEndDayAsync();
            await CheckLunchSoonWarningAsync();
            await CheckLunchStartWarningAsync();
            var activeToday = await _repository.GetActiveTodayAsync(_workerId);
            var text = $"[{_currentState}] Activo: {Format(activeToday)}";

            _trayService?.UpdateTooltip(text);
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Estado: {_currentState} | Activo hoy: {Format(activeToday)}");
        }

        public async Task SetStateAsync(WorkState state)
        {
            var category = state switch
            {
                WorkState.Working or WorkState.Overtime => StateCategory.Active,
                WorkState.Break or WorkState.WC or WorkState.Lunch => StateCategory.Neutral,
                WorkState.Idle => StateCategory.Inactive,
                _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Estado no manejado")
            };
            if (state == WorkState.Overtime)
            {
                _isOvertimeConfirmed = true;
                _shutdownAt = null;
                _shutdownFiveMinNotified = false;
                _shutdownMinuteNotified  = false;
            }
            else if (_isOvertimeConfirmed)
            {
                _isOvertimeConfirmed = false;
                _endDaySoonNotified    = false;
                _endDayReachedNotified = false;
                _shutdownFiveMinNotified = false;
                _shutdownMinuteNotified  = false;
            }
            await LogStateAsync(category, state, StateType.Manual);
        }

        private async Task LogStateAsync(StateCategory category, WorkState state, StateType type)
        {
            _currentCategory = category;
            _currentState = state;
            _currentType = type;
            await _repository.LogStateAsync(_workerId, _workerUserName, category, state, type);
        }

        private string Format(TimeSpan t) =>
            $"{(int)t.TotalHours:D2}h {t.Minutes:D2}m {t.Seconds:D2}s";

        public string GetCurrentStateInfo()
        {
            return $"Estado actual: {_currentState} | Categoría: {_currentCategory} | Tipo: {_currentType} | Tiempo de hoy: {Format(_repository.GetActiveTodayAsync(_workerId).Result)}";
        }


    }



}