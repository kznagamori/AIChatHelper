// WindowFactory.cs
using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace AIChatHelper.Core.Factory;
public class WindowFactory : IWindowFactory
{
    private readonly IServiceProvider _serviceProvider;

    public WindowFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public T CreateWindow<T>() where T : Window
    {
        return _serviceProvider.GetRequiredService<T>();
    }
}
