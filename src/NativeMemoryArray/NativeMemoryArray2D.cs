//-----------------------------------------------------------------------
// <copyright file="NativeChunkedList.cs" company="Jackson Dunstan">
//     Copyright (c) Jackson Dunstan. See LICENSE.md.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Cysharp.Collections
{
    /// <summary>
    /// A two-dimensional array stored in native memory
    /// </summary>
    /// 
    /// <typeparam name="T">
    /// Type of elements in the array
    /// </typeparam>
    [DebuggerTypeProxy(typeof(NativeMemoryArray2DDebugView<>))]
    public sealed unsafe class NativeMemoryArray2D<T> : IDisposable
        where T : unmanaged
    {
        public static readonly NativeMemoryArray2D<T> Empty;
        /// <summary>
        /// Pointer to the memory the array is stored in.
        /// </summary>
        internal readonly byte* m_Buffer;

        readonly bool addMemoryPressure;
        /// <summary>
        /// Length of the array's first dimension
        /// </summary>
        readonly private long m_Length0;

        /// <summary>
        /// Length of the array's second dimension
        /// </summary>
        readonly private long m_Length1;

        public long Length0 => m_Length0;

        public long Length1 => m_Length1;

        static NativeMemoryArray2D()
        {
            Empty = new NativeMemoryArray2D<T>(0,0);
            Empty.Dispose();
        }

        /// <summary>
        /// Create the array and optionally clear it
        /// </summary>
        /// 
        /// <param name="length0">
        /// Length of the array's first dimension. Must be positive.
        /// </param>
        /// 
        /// <param name="length1">
        /// Length of the array's second dimension. Must be positive.
        /// </param>
        /// 
        /// <param name="allocator">
        /// Allocator to allocate native memory with. Must be valid as defined
        /// by <see cref="UnsafeUtility.IsValidAllocator"/>.
        /// </param>
        /// 
        /// <param name="options">
        /// Whether the array should be cleared or not
        /// </param>
        public NativeMemoryArray2D(
            long length0,
            long length1, bool skipZeroClear = false, bool addMemoryPressure = false)
        {
            this.isDisposed = false;
            this.m_Length0 = length0;
            this.m_Length1 = length1;
            this.addMemoryPressure = addMemoryPressure;

            long length = length0 * length1;
            if (length == 0)
            {
#if UNITY_2019_1_OR_NEWER
                m_Buffer = (byte*)Unsafe.AsPointer(ref Unsafe.AsRef<byte>(null));
#else
                m_Buffer = (byte*)Unsafe.AsPointer(ref Unsafe.NullRef<byte>());
#endif
            }
            else
            {
                var allocSize = length * Unsafe.SizeOf<T>();
#if NET6_0_OR_GREATER
                if (skipZeroClear)
                {
                    m_Buffer = (byte*)NativeMemory.Alloc(checked((nuint)length), (nuint)Unsafe.SizeOf<T>());
                }
                else
                {
                    m_Buffer = (byte*)NativeMemory.AllocZeroed(checked((nuint)length), (nuint)Unsafe.SizeOf<T>());
                }
#else
                m_Buffer = (byte*)Marshal.AllocHGlobal((IntPtr)allocSize);
                if (!skipZeroClear)
                {
                    foreach (var span in this)
                    {
                        span.Clear();
                    }
                }
#endif
                if (addMemoryPressure)
                {
                    GC.AddMemoryPressure(allocSize);
                }
            }
        }

        public NativeMemoryArray2D(void* ptr, long length0, long length1, bool addMemoryPressure = false)
        {
            this.isDisposed = false;
            this.m_Length0 = length0;
            this.m_Length1 = length1;
            long length = length0 * length1;

            this.addMemoryPressure = addMemoryPressure;
            m_Buffer = (byte*)ptr;
            if (addMemoryPressure)
            {
                var allocSize = length * Unsafe.SizeOf<T>();
                GC.AddMemoryPressure(allocSize);
            }
        }

        /// <summary>
        /// Get the total number of elements in the array
        /// </summary>
        public long Length
        {
            get
            {
                return m_Length0 * m_Length1;
            }
        }

        /// <summary>
        /// Index into the array to read or write an element
        /// </summary>
        /// 
        /// <param name="index0">
        /// Index of the first dimension
        /// </param>
        /// 
        /// <param name="index1">
        /// Index of the second dimension
        /// </param>
        public ref T this[long index0, long index1]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                long index = index1 * m_Length0 + index0;
                if ((ulong)index >= (ulong)Length) ThrowHelper.ThrowIndexOutOfRangeException();
                var memoryIndex = index * Unsafe.SizeOf<T>();
                return ref Unsafe.AsRef<T>(m_Buffer + memoryIndex);
            }
        }

        public Span<T> AsSpan()
        {
            return AsSpan(0);
        }

        public Span<T> AsSpan(long start)
        {
            if ((ulong)start > (ulong)Length) ThrowHelper.ThrowArgumentOutOfRangeException(nameof(start));
            return AsSpan(start, checked((int)(Length - start)));
        }

        public Span<T> AsSpan(long start, int length)
        {
            if ((ulong)(start + length) > (ulong)this.Length) ThrowHelper.ThrowArgumentOutOfRangeException(nameof(length));
            return new Span<T>(m_Buffer + start * Unsafe.SizeOf<T>(), length);
        }

        /// <summary>
        /// Check if the underlying unmanaged memory has been created and not
        /// freed via a call to <see cref="Dispose"/>.
        /// 
        /// This operation has no access requirements.
        ///
        /// This operation is O(1).
        /// </summary>
        /// 
        /// <value>
        /// Initially true when a non-default constructor is called but
        /// initially false when the default constructor is used. After
        /// <see cref="Dispose"/> is called, this becomes false. Note that
        /// calling <see cref="Dispose"/> on one copy of this object doesn't
        /// result in this becoming false for all copies if it was true before.
        /// This property should <i>not</i> be used to check whether the object
        /// is usable, only to check whether it was <i>ever</i> usable.
        /// </value>
        public bool IsCreated
        {
            get
            {
                return (IntPtr)m_Buffer != IntPtr.Zero;
            }
        }

        bool isDisposed;

        /// <summary>
        /// Release the object's unmanaged memory. Do not use it after this. Do
        /// not call <see cref="Dispose"/> on copies of the object either.
        /// 
        /// This operation requires write access.
        /// 
        /// This complexity of this operation is O(1) plus the allocator's
        /// deallocation complexity.
        /// </summary>
        ///
        public void Dispose()
        {
            DisposeCore();
            GC.SuppressFinalize(this);
        }

        void DisposeCore()
        {
            if (!isDisposed)
            {
                isDisposed = true;
#if UNITY_2019_1_OR_NEWER
                if (buffer == null) return;
#else
                if (Unsafe.IsNullRef(ref Unsafe.AsRef<byte>(m_Buffer))) return;
#endif

#if NET6_0_OR_GREATER
                NativeMemory.Free(m_Buffer);
#else
                Marshal.FreeHGlobal((IntPtr)m_Buffer);
#endif
                if (addMemoryPressure)
                {
                    GC.RemoveMemoryPressure(Length * Unsafe.SizeOf<T>());
                }
                //m_Buffer = null;
                //m_Length0 = 0;
                //m_Length1 = 0;
            }
        }
        /*
        public nint GetBuffer()
        {
            return (nint)buffer;
        }
        */
        
        ~NativeMemoryArray2D()
        {
            DisposeCore();
        }
        

        /// <summary>
        /// Copy the elements of this array to a newly-created managed array
        /// </summary>
        /// 
        /// <returns>
        /// A newly-created managed array with the elements of this array.
        /// </returns>
        public T[,] ToArray()
        {
            T[,] dst = new T[m_Length0, m_Length1];
            Copy(this, dst);
            return dst;
        }

        /// <summary>
        /// Get an enumerator for this array
        /// </summary>
        /// 
        /// <returns>
        /// An enumerator for this array.
        /// </returns>
        public SpanSequence GetEnumerator()
        {
            return AsSpanSequence(int.MaxValue);
        }

        public struct SpanSequence
        {
            readonly NativeMemoryArray2D<T> nativeArray;
            readonly int chunkSize;
            long index;
            long sliceStart;

            internal SpanSequence(NativeMemoryArray2D<T> nativeArray, int chunkSize)
            {
                this.nativeArray = nativeArray;
                index = 0;
                sliceStart = 0;
                this.chunkSize = chunkSize;
            }

            public SpanSequence GetEnumerator() => this;

            public Span<T> Current
            {
                get
                {
                    return nativeArray.AsSpan(sliceStart, (int)Math.Min(chunkSize, nativeArray.Length - sliceStart));
                }
            }

            public bool MoveNext()
            {
                if (index < nativeArray.Length)
                {
                    sliceStart = index;
                    index += chunkSize;
                    return true;
                }
                return false;
            }
        }

        public SpanSequence AsSpanSequence(int chunkSize = int.MaxValue)
        {
            return new SpanSequence(this, chunkSize);
        }

        /*
        
        /// <summary>
        /// Check if this array points to the same native memory as another
        /// array. 
        /// </summary>
        /// 
        /// <param name="other">
        /// Array to check against.
        /// </param>
        /// 
        /// <returns>
        /// If this array points to the same native memory as the given array.
        /// </returns>
        public bool Equals(NativeMemoryArray2D<T> other)
        {
            return m_Buffer == other.m_Buffer
                   && m_Length0 == other.m_Length0
                   && m_Length1 == other.m_Length1;
        }

        /// <summary>
        /// Check if this array points to the same native memory as another
        /// array. 
        /// </summary>
        /// 
        /// <param name="other">
        /// Array to check against.
        /// </param>
        /// 
        /// <returns>
        /// If this array points to the same native memory as the given array.
        /// </returns>
        public override bool Equals(object other)
        {
            if (ReferenceEquals(null, other))
            {
                return false;
            }
            return other is NativeMemoryArray2D<T> && Equals((NativeMemoryArray2D<T>)other);
        }

      

        /// <summary>
        /// Check if two arrays point to the same native memory.
        /// </summary>
        /// 
        /// <param name="a">
        /// First array to check
        /// </param>
        ///
        /// <param name="b">
        /// Second array to check
        /// </param>
        /// 
        /// <returns>
        /// If the given arrays point to the same native memory.
        /// </returns>
        /// 
        public static bool operator ==(NativeMemoryArray2D<T> a, NativeMemoryArray2D<T> b)
        {
            return a.Equals(b);
        }

        /// <summary>
        /// Check if two arrays don't point to the same native memory.
        /// </summary>
        /// 
        /// <param name="a">
        /// First array to check
        /// </param>
        ///
        /// <param name="b">
        /// Second array to check
        /// </param>
        /// 
        /// <returns>
        /// If the given arrays don't point to the same native memory.
        /// </returns>
        public static bool operator !=(NativeMemoryArray2D<T> a, NativeMemoryArray2D<T> b)
        {
            return !a.Equals(b);
        }

        */
        /// <summary>
        /// Copy a native array's elements to another native array
        /// </summary>
        /// 
        /// <param name="src">
        /// Array to copy from
        /// </param>
        /// 
        /// <param name="dest">
        /// Array to copy to
        /// </param>
        /// 
        /// <exception cref="ArgumentException">
        /// If the arrays have different sizes
        /// </exception>
        private static void Copy(NativeMemoryArray2D<T> src, NativeMemoryArray2D<T> dest)
        {
            //src.RequireReadAccess();
            //dest.RequireWriteAccess();
            
            if (src.Length0 != dest.Length0
                || src.Length1 != dest.Length1)
            {
                throw new ArgumentException("Arrays must have the same size");
            }
            
            for (int index0 = 0; index0 < src.Length0; ++index0)
            {
                for (int index1 = 0; index1 < src.Length1; ++index1)
                {
                    dest[index0, index1] = src[index0, index1];
                }
            }
        }

        /// <summary>
        /// Copy a managed array's elements to a native array
        /// </summary>
        /// 
        /// <param name="src">
        /// Array to copy from
        /// </param>
        /// 
        /// <param name="dest">
        /// Array to copy to
        /// </param>
        /// 
        /// <exception cref="ArgumentException">
        /// If the arrays have different sizes
        /// </exception>
        private static void Copy(T[,] src, NativeMemoryArray2D<T> dest)
        {
            //dest.RequireWriteAccess();
            
            if (src.GetLength(0) != dest.Length0
                || src.GetLength(1) != dest.Length1)
            {
                throw new ArgumentException("Arrays must have the same size");
            }
            
            for (int index0 = 0; index0 < dest.Length0; ++index0)
            {
                for (int index1 = 0; index1 < dest.Length1; ++index1)
                {
                    dest[index0, index1] = src[index0, index1];
                }
            }
        }

        /// <summary>
        /// Copy a native array's elements to a managed array
        /// </summary>
        /// 
        /// <param name="src">
        /// Array to copy from
        /// </param>
        /// 
        /// <param name="dest">
        /// Array to copy to
        /// </param>
        /// 
        /// <exception cref="ArgumentException">
        /// If the arrays have different sizes
        /// </exception>
        private static void Copy(NativeMemoryArray2D<T> src, T[,] dest)
        {
            //src.RequireReadAccess();
            
            if (src.Length0 != dest.GetLength(0)
                || src.Length1 != dest.GetLength(1))
            {
                throw new ArgumentException("Arrays must have the same size");
            }

            for (int index0 = 0; index0 < src.Length0; ++index0)
            {
                for (int index1 = 0; index1 < src.Length1; ++index1)
                {
                    dest[index0, index1] = src[index0, index1];
                }
            }
        }
    }
    
    /// <summary>
    /// A debugger view of the array type
    /// </summary>
    /// 
    /// <typeparam name="T">
    /// Type of elements in the array
    /// </typeparam>
    internal sealed class NativeMemoryArray2DDebugView<T>
        where T : unmanaged
    {
        /// <summary>
        /// The array to view
        /// </summary>
        private readonly NativeMemoryArray2D<T> m_Array;

        /// <summary>
        /// Create the view
        /// </summary>
        /// 
        /// <param name="array">
        /// The array to view
        /// </param>
        public NativeMemoryArray2DDebugView(NativeMemoryArray2D<T> array)
        {
            m_Array = array;
        }

        /// <summary>
        /// Get the elements of the array as a managed array
        /// </summary>
        public T[,] Items
        {
            get
            {
                return m_Array.ToArray();
            }
        }
    }
}
