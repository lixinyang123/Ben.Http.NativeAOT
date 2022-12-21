using Jab;

namespace DependencyInjection
{

    [ServiceProvider]
    [Singleton(typeof(IService), typeof(MyService))]
    internal partial class MyServiceProvider { }
}
