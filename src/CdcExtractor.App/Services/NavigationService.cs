namespace CdcExtractor.App.Services;

public sealed class NavigationService
{
    public event Action<Type>? NavigationRequested;

    public void NavigateTo<T>() where T : class => NavigationRequested?.Invoke(typeof(T));

    public void NavigateTo(Type pageType) => NavigationRequested?.Invoke(pageType);
}
