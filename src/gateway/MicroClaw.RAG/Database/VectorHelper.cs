using System.Runtime.InteropServices;

namespace MicroClaw.RAG;

/// <summary>
/// float[] ↔ byte[] 序列化 + 余弦相似度计算。
/// </summary>
internal static class VectorHelper
{
    /// <summary>IEEE 754 小端 float[] → byte[]（零拷贝）。</summary>
    public static byte[] ToBytes(ReadOnlySpan<float> vector)
    {
        byte[] blob = new byte[vector.Length * sizeof(float)];
        MemoryMarshal.AsBytes(vector).CopyTo(blob);
        return blob;
    }

    /// <summary>byte[] → float[]（零拷贝）。</summary>
    public static float[] ToFloats(ReadOnlySpan<byte> blob)
    {
        if (blob.Length % sizeof(float) != 0)
            throw new ArgumentException("blob 长度必须是 4 的整数倍", nameof(blob));

        var floats = new float[blob.Length / sizeof(float)];
        MemoryMarshal.Cast<byte, float>(blob).CopyTo(floats);
        return floats;
    }

    /// <summary>计算两个等长向量的余弦相似度，返回 [-1, 1]。</summary>
    public static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException("向量维度不匹配");

        if (a.Length == 0) return 0f;

        float dot = 0f, normA = 0f, normB = 0f;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        float denom = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denom == 0f ? 0f : dot / denom;
    }
}
