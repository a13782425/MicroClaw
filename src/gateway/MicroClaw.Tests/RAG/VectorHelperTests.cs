using FluentAssertions;
using MicroClaw.RAG;

namespace MicroClaw.Tests.RAG;

public class VectorHelperTests
{
    // ── ToBytes / ToFloats 往返 ──

    [Fact]
    public void RoundTrip_FloatArray()
    {
        float[] original = [1.0f, -2.5f, 3.14159f, 0f, float.MaxValue];
        byte[] blob = VectorHelper.ToBytes(original);
        float[] restored = VectorHelper.ToFloats(blob);
        restored.Should().Equal(original);
    }

    [Fact]
    public void ToBytes_Empty_Returns_Empty()
    {
        VectorHelper.ToBytes([]).Should().BeEmpty();
    }

    [Fact]
    public void ToFloats_Empty_Returns_Empty()
    {
        VectorHelper.ToFloats([]).Should().BeEmpty();
    }

    [Fact]
    public void ToFloats_Invalid_Length_Throws()
    {
        var act = () => VectorHelper.ToFloats(new byte[] { 1, 2, 3 });
        act.Should().Throw<ArgumentException>();
    }

    // ── 余弦相似度 ──

    [Fact]
    public void CosineSimilarity_Identical_Vectors_Returns_One()
    {
        float[] a = [1f, 2f, 3f];
        VectorHelper.CosineSimilarity(a, a).Should().BeApproximately(1.0f, 1e-5f);
    }

    [Fact]
    public void CosineSimilarity_Opposite_Vectors_Returns_NegativeOne()
    {
        float[] a = [1f, 0f, 0f];
        float[] b = [-1f, 0f, 0f];
        VectorHelper.CosineSimilarity(a, b).Should().BeApproximately(-1.0f, 1e-5f);
    }

    [Fact]
    public void CosineSimilarity_Orthogonal_Vectors_Returns_Zero()
    {
        float[] a = [1f, 0f, 0f];
        float[] b = [0f, 1f, 0f];
        VectorHelper.CosineSimilarity(a, b).Should().BeApproximately(0f, 1e-5f);
    }

    [Fact]
    public void CosineSimilarity_DifferentLengths_Throws()
    {
        float[] a = [1f, 2f];
        float[] b = [1f, 2f, 3f];
        var act = () => VectorHelper.CosineSimilarity(a, b);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CosineSimilarity_Empty_Returns_Zero()
    {
        VectorHelper.CosineSimilarity([], []).Should().Be(0f);
    }

    [Fact]
    public void CosineSimilarity_Zero_Vector_Returns_Zero()
    {
        float[] a = [0f, 0f, 0f];
        float[] b = [1f, 2f, 3f];
        VectorHelper.CosineSimilarity(a, b).Should().Be(0f);
    }

    [Fact]
    public void CosineSimilarity_Scaled_Vectors_Are_Equal()
    {
        float[] a = [1f, 2f, 3f];
        float[] b = [2f, 4f, 6f]; // 2x scale of a
        VectorHelper.CosineSimilarity(a, b).Should().BeApproximately(1.0f, 1e-5f);
    }
}
