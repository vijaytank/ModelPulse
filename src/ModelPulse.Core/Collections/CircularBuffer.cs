using System;
using System.Collections;
using System.Collections.Generic;

namespace ModelPulse.Core.Collections
{
    /// <summary>
    /// A fixed-size circular buffer that rolls over old elements when full.
    /// Mathematically guarantees bounded memory consumption (NFR-01).
    /// </summary>
    public class CircularBuffer<T> : IEnumerable<T>
    {
        private readonly T[] _buffer;
        private int _start;
        private int _end;
        private int _size;
        private readonly int _capacity;

        public CircularBuffer(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentException("Capacity must be greater than zero.", nameof(capacity));
            }
            _capacity = capacity;
            _buffer = new T[capacity];
            _start = 0;
            _end = 0;
            _size = 0;
        }

        public int Count => _size;
        public int Capacity => _capacity;

        public void Enqueue(T item)
        {
            _buffer[_end] = item;
            _end = (_end + 1) % _capacity;

            if (_size == _capacity)
            {
                _start = (_start + 1) % _capacity; // overwrite oldest
            }
            else
            {
                _size++;
            }
        }

        public T Dequeue()
        {
            if (_size == 0)
            {
                throw new InvalidOperationException("Buffer is empty.");
            }
            T item = _buffer[_start];
            _buffer[_start] = default!;
            _start = (_start + 1) % _capacity;
            _size--;
            return item;
        }

        public T Peek()
        {
            if (_size == 0)
            {
                throw new InvalidOperationException("Buffer is empty.");
            }
            return _buffer[_start];
        }

        public void Clear()
        {
            Array.Clear(_buffer, 0, _buffer.Length);
            _start = 0;
            _end = 0;
            _size = 0;
        }

        public IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < _size; i++)
            {
                yield return _buffer[(_start + i) % _capacity];
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
