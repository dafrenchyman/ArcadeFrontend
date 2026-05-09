using System;
using System.Collections.Generic;

namespace ArcadeFrontend;

public class MenuPath
{
    public List<int> Indices { get; } = new();

    public MenuPath() { }

    public MenuPath(IEnumerable<int> indices)
    {
        Indices.AddRange(indices);
    }

    public int Length => Indices.Count;
    public int this[int i] => Indices[i];

    public override string ToString() => $"[{string.Join(", ", Indices)}]";
    
    public int this[Index i]
    {
        get
        {
            int idx = i.IsFromEnd ? Indices.Count - i.Value : i.Value;
            return Indices[idx];
        }
        set
        {
            int idx = i.IsFromEnd ? Indices.Count - i.Value : i.Value;
            Indices[idx] = value;
        }
    }
    
    public void RemoveLast()
    {
        if (Indices.Count == 0)
            throw new InvalidOperationException("MenuPath is empty.");

        Indices.RemoveAt(Indices.Count - 1);
    }
}