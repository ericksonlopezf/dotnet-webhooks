// Copyright © Erickson Lopez. MIT License.
using BenchmarkDotNet.Running;

namespace EricksonLopez.Webhooks.Benchmarks;

public static class Program
{
    public static void Main(string[] args)
    {
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
