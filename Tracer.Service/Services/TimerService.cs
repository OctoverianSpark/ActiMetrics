using Tracer.Data;
using Tracer.Shared.Models;

namespace Tracer.Service.Services
{
    public class TimerService
    {
        private readonly StateRepository _repository;
        private readonly ActivityService _activityService;
        private readonly string _workerId;

        private StateCategory _currentCategory = StateCategory.Active;
        private WorkState _currentState = WorkState.Working;

        public TimerService(StateRepository repository, ActivityService activityService)
        {
            _repository = repository;
            _activityService = activityService;
            _workerId = Environment.MachineName;
        }

        public async Task InitializeAsync()
        {
            await LogStateAsync(StateCategory.Active, WorkState.Working);
            Console.WriteLine($"[Tracer] Iniciado | Worker: {_workerId}");
        }

        // Llamado cada segundo desde el Worker
        public async Task Tick()
        {
            bool isIdle = _activityService.IsIdle();

            // Detecta cambio automático Active → Idle
            if (isIdle && _currentCategory == StateCategory.Active)
            {
                await LogStateAsync(StateCategory.Inactive, WorkState.Idle);
            }
            // Detecta regreso automático Idle → Working
            else if (!isIdle && _currentState == WorkState.Idle)
            {
                await LogStateAsync(StateCategory.Active, WorkState.Working);
            }

            var activeToday = await _repository.GetActiveTodayAsync(_workerId);
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Estado: {_currentState} | Activo hoy: {Format(activeToday)}");
        }

        // Llamado desde el system tray cuando el usuario cambia estado manualmente
        public async Task SetStateAsync(WorkState state)
        {
            var category = state switch
            {
                WorkState.Working or WorkState.Meeting or WorkState.OnCall => StateCategory.Active,
                _ => StateCategory.Inactive
            };

            await LogStateAsync(category, state);
        }

        private async Task LogStateAsync(StateCategory category, WorkState state)
        {
            _currentCategory = category;
            _currentState = state;
            await _repository.LogStateAsync(_workerId, category, state);
        }

        private string Format(TimeSpan t) =>
            $"{(int)t.TotalHours:D2}h {t.Minutes:D2}m {t.Seconds:D2}s";
    }
}