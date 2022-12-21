namespace DependencyInjection
{
    internal interface IService
    {
        string Message();
    }

    internal class MyService : IService
    {
        public string Message() => "Compile Time Dependency Injection";
    }
}
