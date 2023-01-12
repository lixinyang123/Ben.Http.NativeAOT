# Ben.Http.NativeAOT

[![NuGet version (Ben.Http)](https://img.shields.io/nuget/v/Ben.Http.svg?style=flat-square)](https://www.nuget.org/packages/Ben.Http/)

Low level ASP.NET Core example web server with NativeAOT. `~3MB size`

An example of using the ASP.NET Core servers for a .NET web application without any of the rest of the framework (e.g. Hosting, middleware etc). So you can create your own distinct opinionated framework.

## Using Ben.Http

Mostly its an example to derive from. 

`src\Ben.Http` contains and sets up the server; it is [Kestrel](https://github.com/dotnet/aspnetcore/tree/master/src/Servers/Kestrel) by default, but any server deriving from 'IServer' will also work (e.g. `HttpSys`, `IIS`, etc)

`HttpServer.cs` contains the server that is newed up; currently set to various defaults/

`HttpContext.cs` contains the Request/Response context, but this can be changed to be whatever set of properties you want to expose; in whatever way you want to expose them. Generally you get the information from the server by asking the `IFeatureCollection` for them and they are in the namespace `Microsoft.AspNetCore.Http.Features`

`HttpApplication.cs` is the application that deals with processing the requests. This creates and disposes of the `HttpContext` setting its features; and in this example `Task ProcessRequestAsync(HttpContext context)` is `abstract` so an application can derive from this class and implement that one method; and the bolierplate of setting up the Request/Response context is handled for them.

## Building

### Prerequisites

Windows

```bash
Visual Studio 2022, including Desktop development with C++ workload.
```

Ubuntu (20.04+)

```bash
sudo apt-get install libicu-dev cmake
```

### Build

```bash
dotnet build
```

### Publish with NativeAOT

- Windows
  ```bash
  dotnet publish -r win-x64 -c Release
  dotnet publish -r win-arm64 -c Release
  ```
 
- Linux
   ```bash
  dotnet publish -r linux-x64 -c Release
  dotnet publish -r linux-arm64 -c Release
  ```
  
- MacOS
  ```bash
  dotnet publish -r osx-x64 -c Release
  dotnet publish -r osx-arm64 -c Release
  ```

## Related Projects

You should take a look at these related projects:

- [.NET 7](https://github.com/dotnet/runtime)
- [ASP.NET](https://github.com/aspnet)
- [NativeAOT](https://github.com/dotnet/runtime/tree/main/src/coreclr/nativeaot)
- [Ben.Http](https://github.com/benaadams/Ben.Http)
- [PublishAotCompressed](https://github.com/MichalStrehovsky/PublishAotCompressed)
- [jab](https://github.com/pakrym/jab)
