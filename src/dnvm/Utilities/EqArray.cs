
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using Serde;

namespace Dnvm;

public static class EqArray
{
    public static EqArray<T> ToEq<T>(this IEnumerable<T> array) => new(array.ToImmutableArray());
    public static EqArray<T> ToEq<T>(this ImmutableArray<T> array) => new(array);
    public static EqArray<T> Create<T>(params T[] array) => ImmutableArray.Create(array).ToEq();
    public static EqArray<T> Create<T>(ReadOnlySpan<T> span) => ImmutableArray.Create(span).ToEq();
}

[SerdeWrap(typeof(EqArraySerdeWrap))]
[CollectionBuilder(typeof(EqArray), nameof(EqArray.Create))]
public readonly struct EqArray<T>(ImmutableArray<T> value) : IReadOnlyCollection<T>, IEquatable<EqArray<T>>
{
    public ImmutableArray<T> Array => value;

    public static readonly EqArray<T> Empty = ImmutableArray<T>.Empty.ToEq();

    public int Length => value.Length;

    int IReadOnlyCollection<T>.Count => value.Length;

    public override bool Equals(object? obj) => obj is EqArray<T> other && Equals(other);

    public bool Equals(EqArray<T> other) => value.SequenceEqual(other.Array);

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)value).GetEnumerator();

    public override int GetHashCode()
    {
        return value.Aggregate(0, (acc, item) => HashCode.Combine(acc, item));
    }

    public override string ToString()
    {
        return "[ " + string.Join(", ", value) + " ]";
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return ((IEnumerable)value).GetEnumerator();
    }

    public T this[int index] => value[index];

    public EqArray<T> Add(T item) => new(value.Add(item));

    public EqArray<T> Replace(T oldItem, T newItem) => new(value.Replace(oldItem, newItem));
}

public static class EqArraySerdeWrap
{
    public readonly record struct SerializeImpl<T, TWrap>(EqArray<T> Value) : ISerialize
        where TWrap : struct, ISerialize, ISerializeWrap<T, TWrap>
    {
        public void Serialize(ISerializer serializer)
        {
            EnumerableHelpers.SerializeSpan<T, TWrap>(typeof(EqArray<T>).Name, Value.Array.AsSpan(), serializer);
        }
    }
    public readonly record struct DeserializeImpl<T, TWrap> : IDeserialize<EqArray<T>>
        where TWrap : IDeserialize<T>
    {
        static EqArray<T> IDeserialize<EqArray<T>>.Deserialize<D>(ref D deserializer)
        {
            return deserializer.DeserializeEnumerable<
                EqArray<T>,
                Visitor>(new Visitor());
        }

        private struct Visitor : IDeserializeVisitor<EqArray<T>>
        {
            public string ExpectedTypeName => typeof(ImmutableArray<T>).ToString();
            EqArray<T> IDeserializeVisitor<EqArray<T>>.VisitEnumerable<D>(ref D d)
            {
                ImmutableArray<T>.Builder builder;
                if (d.SizeOpt is int size)
                {
                    builder = ImmutableArray.CreateBuilder<T>(size);
                }
                else
                {
                    size = -1; // Set initial size to unknown
                    builder = ImmutableArray.CreateBuilder<T>();
                }

                while (d.TryGetNext<T, TWrap>(out T? next))
                {
                    builder.Add(next);
                }
                if (size >= 0 && builder.Count != size)
                {
                    throw new InvalidDeserializeValueException($"Expected {size} items, found {builder.Count}");
                }
                return new(builder.ToImmutable());
            }
        }
    }
}