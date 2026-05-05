using Photino.NET;

namespace Firestarter.App.Ipc;

public class WindowAccessor
{
    PhotinoWindow? _window;

    public PhotinoWindow Window => _window ?? throw new InvalidOperationException("Window not bound");

    public void Bind(PhotinoWindow window) => _window = window;
}
