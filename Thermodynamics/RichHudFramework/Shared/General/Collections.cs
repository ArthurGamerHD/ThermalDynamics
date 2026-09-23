using System;
using System.Collections;
using System.Collections.Generic;

namespace RichHudFramework
{
	public interface IIndexedCollection<T>
	{
		T this[int index] { get; }

		int Count { get; }
	}

	public class CollectionDataEnumerator<T> : IEnumerator<T>
	{

		public T Current => Getter(index);

		object IEnumerator.Current => Current;

		private readonly Func<int, T> Getter;
		private readonly Func<int> CountFunc;
		private int index = -1;


		public CollectionDataEnumerator(Func<int, T> getter, Func<int> countFunc)
		{
			Getter = getter;
			CountFunc = countFunc;
		}


		public bool MoveNext()
		{
			index++;
			return index < CountFunc();
		}


		public void Reset() => index = -1;


		public void Dispose() { }
	}
}