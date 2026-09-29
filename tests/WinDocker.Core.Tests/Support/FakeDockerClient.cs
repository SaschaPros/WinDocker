using System.Reflection;
using Docker.DotNet;

namespace WinDocker.Core.Tests.Support;

/// <summary>
/// A programmable <see cref="IDockerClient"/> built on <see cref="DispatchProxy"/>. Every operation
/// (<c>client.Containers.ListContainersAsync(...)</c>) goes to one handler that gets the method and its arguments.
/// The handler returns the result (not wrapped in a task) or throws to fail the call.
/// </summary>
internal class FakeDockerClient : DispatchProxy
{
    private Func<MethodInfo, object?[], object?> handler = (method, _) => throw new NotSupportedException(method.Name);

    /// <summary>Set to true by <see cref="IDisposable.Dispose"/>.</summary>
    public bool Disposed { get; private set; }

    public static IDockerClient Create(Func<MethodInfo, object?[], object?> handler)
    {
        var client = Create<IDockerClient, FakeDockerClient>();
        ((FakeDockerClient)(object)client).handler = handler;
        return client;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        args ??= [];

        if (targetMethod.Name == nameof(IDisposable.Dispose))
        {
            Disposed = true;
            return null;
        }

        // client.Containers, client.Images, ...: hand out a proxy for the operations interface that shares the handler.
        if (targetMethod.Name.StartsWith("get_", StringComparison.Ordinal) && targetMethod.ReturnType.IsInterface)
        {
            var operations = (FakeDockerClient)Create(targetMethod.ReturnType, typeof(FakeDockerClient))!;
            operations.handler = handler;
            return operations;
        }

        try
        {
            return Wrap(targetMethod.ReturnType, handler(targetMethod, args));
        }
        catch (Exception exception)
        {
            return Fail(targetMethod.ReturnType, exception);
        }
    }

    private static object? Wrap(Type returnType, object? result)
    {
        if (returnType == typeof(Task))
        {
            return result as Task ?? Task.CompletedTask;
        }

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            return result is Task
                ? result
                : typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(returnType.GetGenericArguments()[0]).Invoke(null, [result]);
        }

        return result;
    }

    private static object? Fail(Type returnType, Exception exception)
    {
        if (returnType == typeof(Task))
        {
            return Task.FromException(exception);
        }

        return typeof(Task).GetMethod(nameof(Task.FromException), 1, [typeof(Exception)])!
            .MakeGenericMethod(returnType.GetGenericArguments()[0])
            .Invoke(null, [exception]);
    }
}
