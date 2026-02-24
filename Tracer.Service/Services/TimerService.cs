using Tracer.Data;
using Tracer.Shared;
using Tracer.Shared.Models;

namespace Tracer.Service.Services
{
    public class TimerService
    {

        private bool _manualState = false;
        private readonly StateRepository _repository;
        private readonly ActivityService _activityService;
        private readonly string _workerId;
        private ITrayService? _trayService;
        private StateCategory _currentCategory = StateCategory.Inactive;
        private WorkState _currentState = WorkState.Idle;
        private StateType _currentType = StateType.Auto;
        public void SetTrayService(ITrayService trayService)
        {
            _trayService = trayService;
        }
        public TimerService(StateRepository repository, ActivityService activityService)
        {
            _repository = repository;
            _activityService = activityService;
            _workerId = Environment.MachineName;
        }

        public async Task InitializeAsync()
        {
            await LogStateAsync(StateCategory.Active, WorkState.Working,StateType.Manual);
            Console.WriteLine($"[Tracer] Iniciado | Worker: {_workerId}");
        }
        private async Task HandleStateTransition(bool isIdle)
        {
            var newState = (_currentState, _currentType, isIdle) switch
            {
                // Si está activo y se va a idle → Auto Idle
                (_, _, true) when _currentCategory == StateCategory.Active
                    => (StateCategory.Inactive, WorkState.Idle, StateType.Auto),

                // Si estaba en Auto Idle y vuelve actividad → Auto Working
                (WorkState.Idle, StateType.Auto, false)
                    => (StateCategory.Active, WorkState.Working, StateType.Auto),

                // Si está en estado Manual, no cambia automáticamente
                (_, StateType.Manual, _)
                    => ((StateCategory?)null, (WorkState?)null, (StateType?)null),

                _ => ((StateCategory?)null, (WorkState?)null, (StateType?)null)
            };

            if (newState.Item1 is not null)
                await LogStateAsync(newState.Item1.Value, newState.Item2.Value, newState.Item3.Value);
        }

        public async Task Tick()
        {
            bool isIdle = _activityService.IsIdle();
            await HandleStateTransition(isIdle);

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
                _ => StateCategory.Inactive
            };
            await LogStateAsync(category, state, StateType.Manual); // ← siempre Manual
        }

        private async Task LogStateAsync(StateCategory category, WorkState state, StateType type)
        {
            _currentCategory = category;
            _currentState = state;
            _currentType = type;
            await _repository.LogStateAsync(_workerId, category, state, type);
        }
        private string Format(TimeSpan t) =>
            $"{(int)t.TotalHours:D2}h {t.Minutes:D2}m {t.Seconds:D2}s";
    }
}