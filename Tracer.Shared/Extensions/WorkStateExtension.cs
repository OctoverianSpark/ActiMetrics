using System.Reflection;
using Tracer.Shared.Attributes;
using Tracer.Shared.Models;

namespace Tracer.Shared.Extensions
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