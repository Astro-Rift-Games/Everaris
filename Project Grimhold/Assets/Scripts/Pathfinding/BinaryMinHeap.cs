using System;

/// <summary>
/// An indexed binary min-heap backed by preallocated arrays, used by
/// <see cref="AStarPathSolver"/> as the open set.
///
/// Supports O(log n) <see cref="PushOrDecreaseKey"/> so updating the priority of an
/// already-open node preserves A* optimality without heap allocations or duplicate entries.
/// </summary>
internal sealed class BinaryMinHeap
{
    private readonly (int cost, int nodeIndex, int x, int y)[] _data;
    private readonly int[] _heapIndexByNode;
    private int _count;

    /// <summary>Creates a heap with the given preallocated node capacity.</summary>
    internal BinaryMinHeap(int capacity)
    {
        _data = new (int, int, int, int)[capacity];
        _heapIndexByNode = new int[capacity];
        for (int i = 0; i < capacity; i++)
        {
            _heapIndexByNode[i] = -1;
        }
        _count = 0;
    }

    /// <summary>Number of elements currently in the heap.</summary>
    internal int Count => _count;

    /// <summary>Removes all elements in O(Count) without releasing backing arrays.</summary>
    internal void Clear()
    {
        for (int i = 0; i < _count; i++)
        {
            _heapIndexByNode[_data[i].nodeIndex] = -1;
        }
        _count = 0;
    }

    /// <summary>
    /// Inserts a node or decreases its priority if it is already present in the heap.
    /// </summary>
    internal void PushOrDecreaseKey(int cost, int nodeIndex, int x, int y)
    {
        int existingSlot = _heapIndexByNode[nodeIndex];
        if (existingSlot >= 0)
        {
            if (cost < _data[existingSlot].cost)
            {
                _data[existingSlot] = (cost, nodeIndex, x, y);
                BubbleUp(existingSlot);
            }
            return;
        }

        if (_count >= _data.Length)
        {
            throw new InvalidOperationException(
                $"{nameof(BinaryMinHeap)} is full (capacity {_data.Length}). " +
                "Increase MaxPathIterations or the heap capacity.");
        }

        _data[_count] = (cost, nodeIndex, x, y);
        _heapIndexByNode[nodeIndex] = _count;
        BubbleUp(_count);
        _count++;
    }

    /// <summary>Returns the element with the lowest cost without removing it.</summary>
    internal (int cost, int x, int y) Peek()
    {
        var top = _data[0];
        return (top.cost, top.x, top.y);
    }

    /// <summary>Removes and returns the element with the lowest cost.</summary>
    internal (int cost, int x, int y) Pop()
    {
        var top = _data[0];
        _heapIndexByNode[top.nodeIndex] = -1;
        _count--;
        if (_count > 0)
        {
            var last = _data[_count];
            _data[0] = last;
            _heapIndexByNode[last.nodeIndex] = 0;
            PushDown(0);
        }
        return (top.cost, top.x, top.y);
    }

    private void BubbleUp(int index)
    {
        while (index > 0)
        {
            int parent = (index - 1) / 2;
            if (_data[parent].cost <= _data[index].cost) break;
            Swap(parent, index);
            index = parent;
        }
    }

    private void PushDown(int index)
    {
        while (true)
        {
            int left = 2 * index + 1;
            int right = 2 * index + 2;
            int smallest = index;

            if (left < _count && _data[left].cost < _data[smallest].cost) smallest = left;
            if (right < _count && _data[right].cost < _data[smallest].cost) smallest = right;

            if (smallest == index) break;
            Swap(index, smallest);
            index = smallest;
        }
    }

    private void Swap(int a, int b)
    {
        var temp = _data[a];
        _data[a] = _data[b];
        _data[b] = temp;
        _heapIndexByNode[_data[a].nodeIndex] = a;
        _heapIndexByNode[_data[b].nodeIndex] = b;
    }
}
