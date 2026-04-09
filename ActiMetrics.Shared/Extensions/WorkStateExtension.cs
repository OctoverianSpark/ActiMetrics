using System.Reflection;
using ActiMetrics.Shared.Attributes;
using ActiMetrics.Shared.Models;

namespace ActiMetrics.Shared.Extensions
{
    public static class WorkStateExtensions
    {
        public static StateInfoAttribute? GetInfo(this WorkState state)
        {
            return typeof(WorkState)
                .GetField(state.ToString())
                ?.GetCustomAttribute<StateInfoAttribute>();
        }
    }
}