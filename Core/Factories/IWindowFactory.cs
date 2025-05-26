using System;
using System.Windows;

namespace AIChatHelper.Core.Factory;
public interface IWindowFactory
{
    T CreateWindow<T>() where T : Window;
}
