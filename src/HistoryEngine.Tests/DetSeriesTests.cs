using HistoryEngine.Core;
using Xunit;

namespace HistoryEngine.Tests;

/// <summary>
/// The reproducible transcendentals cosmology is built on.
/// </summary>
/// <remarks>
/// These are checked against the runtime's own <c>Math</c> rather than against tables, because the
/// runtime's answers are correct to well under an ULP — they are simply not guaranteed to be the
/// <em>same</em> answers on another machine, which is the whole reason the series exist. A test is
/// free to compare against them; engine code is not.
/// </remarks>
public sealed class DetSeriesTests
{
    private const double Tolerance = 1e-11;

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.3)]
    [InlineData(-0.75)]
    [InlineData(1.5)]
    [InlineData(3.14159)]
    [InlineData(-9.5)]
    [InlineData(42.0)]
    public void ExpMatchesTheRuntime(double x) =>
        Assert.Equal(Math.Exp(x), DetSeries.Exp(x), Math.Abs(Math.Exp(x)) * Tolerance);

    [Theory]
    [InlineData(1e-6)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(2.718281828)]
    [InlineData(1000.0)]
    [InlineData(3.2e10)]
    public void LogMatchesTheRuntime(double x) =>
        Assert.Equal(Math.Log(x), DetSeries.Log(x), Tolerance * 10.0);

    [Theory]
    [InlineData(2.0, 10.0)]
    [InlineData(0.5, -3.0)]
    [InlineData(1.6, 0.37)]
    [InlineData(90.0, 0.5)]
    public void PowMatchesTheRuntime(double x, double y) =>
        Assert.Equal(Math.Pow(x, y), DetSeries.Pow(x, y), Math.Abs(Math.Pow(x, y)) * Tolerance);

    [Theory]
    [InlineData(1e-9)]
    [InlineData(0.008)]
    [InlineData(1.0)]
    [InlineData(27.0)]
    [InlineData(3.5e8)]
    public void CbrtMatchesTheRuntime(double x) =>
        Assert.Equal(Math.Cbrt(x), DetSeries.Cbrt(x), Math.Abs(Math.Cbrt(x)) * Tolerance);

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.4)]
    [InlineData(-1.2)]
    [InlineData(2.9)]
    [InlineData(3.9)]
    [InlineData(-7.7)]
    [InlineData(31.4)]
    public void SinAndCosMatchTheRuntimeEverywhereOnTheCircle(double radians)
    {
        Assert.Equal(Math.Sin(radians), DetSeries.Sin(radians), Tolerance);
        Assert.Equal(Math.Cos(radians), DetSeries.Cos(radians), Tolerance);
    }

    [Fact]
    public void SinAndCosStayOnTheUnitCircle()
    {
        for (int step = -400; step <= 400; step++)
        {
            double radians = step * 0.0257;
            double sin = DetSeries.Sin(radians);
            double cos = DetSeries.Cos(radians);
            Assert.Equal(1.0, (sin * sin) + (cos * cos), 1e-12);
        }
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(-0.42)]
    [InlineData(0.0)]
    [InlineData(0.9999)]
    [InlineData(1.0)]
    public void AsinMatchesTheRuntime(double value) =>
        Assert.Equal(Math.Asin(value), DetSeries.Asin(value), 1e-9);

    [Theory]
    [InlineData(1.0, 1.0)]
    [InlineData(-1.0, 1.0)]
    [InlineData(1.0, -1.0)]
    [InlineData(-1.0, -1.0)]
    [InlineData(0.0, 1.0)]
    [InlineData(3.0, 0.0)]
    [InlineData(0.02, -14.0)]
    public void Atan2MatchesTheRuntimeInEveryQuadrant(double y, double x) =>
        Assert.Equal(Math.Atan2(y, x), DetSeries.Atan2(y, x), 1e-10);

    [Fact]
    public void AsinInvertsSin()
    {
        for (int step = -90; step <= 90; step += 3)
        {
            double radians = DetSeries.ToRadians(step);
            Assert.Equal(radians, DetSeries.Asin(DetSeries.Sin(radians)), 1e-9);
        }
    }

    [Fact]
    public void GaussianDrawsCentreOnTheMean()
    {
        IRng rng = new Pcg32(9_001);
        double sum = 0.0;
        const int draws = 4096;
        for (int i = 0; i < draws; i++)
        {
            sum += DetSeries.Gaussian(rng, 10.0, 2.0);
        }

        Assert.InRange(sum / draws, 9.85, 10.15);
    }

    [Fact]
    public void LogUniformStaysInsideItsBounds()
    {
        IRng rng = new Pcg32(4_242);
        for (int i = 0; i < 512; i++)
        {
            double value = DetSeries.LogUniform(rng, 0.0002, 0.02);
            Assert.InRange(value, 0.0002, 0.02);
        }
    }
}
