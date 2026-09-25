using System;
using System.Collections.Generic;
using VRageMath;

namespace RichHudFramework
{
	public static class CollectionExtensions
	{

		public static void InsertSpan<T>(this List<T> dst, int dstIndex, IReadOnlyList<T> src, int srcIndex = 0, int srcCount = -1)
		{
			if (srcCount == -1) srcCount = src.Count - srcIndex;
			if (srcCount <= 0) return;

			dst.EnsureCapacity(dst.Count + srcCount);

			int dstOverflowStart = Math.Max(dstIndex, dst.Count - srcCount);
			int dstOverflowCount = dst.Count - dstOverflowStart;
			int srcOverflowCount = srcCount - dstOverflowCount;

			int srcOflEnd = srcIndex + srcCount - 1;
			int srcOflStart = srcOflEnd - srcOverflowCount + 1;

			for (int i = srcOflStart; i <= srcOflEnd; i++)
				dst.Add(src[i]);

			for (int i = dstOverflowStart; i < dst.Count; i++)
				dst.Add(dst[i]);

			for (int i = dstIndex; i < dstOverflowStart; i++)
				dst[i + srcCount] = dst[i];

			for (int i = 0; i < srcCount - srcOverflowCount; i++)
				dst[dstIndex + i] = src[srcIndex + i];
		}


		public static T[] GetSubarray<T>(this T[] arr, int start)
		{
			var result = new T[arr.Length - start];
			Array.Copy(arr, start, result, 0, result.Length);
			return result;
		}


		public static T[] GetSubarray<T>(this T[] arr, int start, int end)
		{
			end = MathHelper.Clamp(end, 0, arr.Length);
			if (end <= start) return Array.Empty<T>();

			var result = new T[end - start];
			Array.Copy(arr, start, result, 0, result.Length);
			return result;
		}
	}
}