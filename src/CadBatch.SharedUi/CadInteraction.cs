using System;

namespace CadBatch.UI
{
    public static class CadInteraction
    {
        public static Action<bool>? VisibilityChanged;
        public static IDisposable Begin(){VisibilityChanged?.Invoke(true);return new Scope();}
        private sealed class Scope : IDisposable {public void Dispose(){VisibilityChanged?.Invoke(false);}}
    }
}
