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
            _workerUserName = Environment.UserName;
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
            var startLunch = TimeOnly.ParseExact(programation.Start_Lunch, "HH:mm", null);
            var endLunch = TimeOnly.ParseExact(programation.End_Lunch, "HH:mm", null);
            var gracePeriod = startDay.AddMinutes(5);
            if (now >= startLunch && now < endLunch)
                return (WorkState.Lunch, StateCategory.Neutral, StateType.Auto, false);

            if (now >= endLunch)
                return (WorkState.Working, StateCategory.Active, StateType.Auto, isLate: true);

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
                WorkState.Working => StateCategory.Active,
                WorkState.Break or WorkState.WC or WorkState.Lunch => StateCategory.Neutral,
                WorkState.Idle => StateCategory.Inactive,
                _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Estado no manejado")
            };
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