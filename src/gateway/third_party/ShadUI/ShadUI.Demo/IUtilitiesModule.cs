using Jab;

namespace ShadUI.Demo;

[ServiceProviderModule]
[Singleton<DialogManager>]
[Singleton<ToastManager>]
public interface IUtilitiesModule
{
}