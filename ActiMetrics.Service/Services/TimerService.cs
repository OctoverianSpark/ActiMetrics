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
        private readonly Session _session;
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

        public TimerService(StateRepository repository, ActivityService activityService, SyncService syncService, Session session)
        {
            _repository = repository;
            _activityService = activityService;
            _syncService = syncService;
            _session = session;
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
        private bool _endDayFiveMinNotified = false;
        private bool _endDayReachedNotified = false;
        private bool _isOvertimeConfirmed = false;

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

        // Ya NO apaga el equipo al llegar la hora de salida — solo notifica. Antes, con
        // auto_shutdown_enabled=true para el grupo, el agente ejecutaba `shutdown /s` 60 minutos
        // después de programation.End_Day, lo que cortaba capturas/telemetría aunque la persona
        // siguiera trabajando y no hubiera marcado 'Horas Extras' a tiempo. Ahora el equipo (y con
        // él, las capturas) sigue funcionando indefinidamente después de la hora programada, hasta
        // que alguien lo apague de verdad — _session.AutoShutdownEnabled queda sin efecto acá a
        // propósito, no se borró el campo/preferencia por si se retoma más adelante.
        private Task CheckEndDayAsync()
        {
            if (_todayProgramation is null || string.IsNullOrEmpty(_todayProgramation.End_Day)) return Task.CompletedTask;

            if (_currentState == WorkState.Overtime || _currentState == WorkState.Offline)
                return Task.CompletedTask;

            var ahora = TimeOnly.FromDateTime(DateTime.Now);
            var endDay = TimeOnly.ParseExact(_todayProgramation.End_Day, "HH:mm", null);
            var aviso5m = endDay.AddMinutes(-5);

            if (ahora >= aviso5m && ahora < endDay && !_endDayFiveMinNotified)
            {
                _endDayFiveMinNotified = true;
                _trayService?.Notify(("⚠️ Fin de jornada en 5 minutos",
                    $"Tu jornada termina a las {_todayProgramation.End_Day}. Selecciona 'Horas Extras' en el menú si vas a quedarte."));
                Console.WriteLine($"[Tracer] Aviso fin de jornada (5m): {_todayProgramation.End_Day}");
            }

            if (ahora >= endDay && !_endDayReachedNotified)
            {
                _endDayReachedNotified = true;
                _trayService?.Notify(("🔴 Jornada finalizada", "Recuerda registrar tu salida."));
                Console.WriteLine("[Tracer] Jornada finalizada.");
            }

            return Task.CompletedTask;
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
            }
            else if (_isOvertimeConfirmed)
            {
                _isOvertimeConfirmed = false;
                _endDayFiveMinNotified = false;
                _endDayReachedNotified = false;
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