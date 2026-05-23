namespace Dsam.Core.Analysis.ControlFlow;

public sealed class DominatorAnalysis
{
    public DominatorResult Analyze(ControlFlowGraph graph)
    {
        var blockStarts = graph.Blocks
            .Where(block => block.Kind == BasicBlockKind.Internal)
            .Select(block => block.StartAddress)
            .ToArray();

        if (blockStarts.Length == 0)
        {
            return new DominatorResult(
                new Dictionary<ulong, HashSet<ulong>>(),
                Array.Empty<BackEdge>(),
                Array.Empty<NaturalLoop>(),
                Array.Empty<ConditionalRegion>());
        }

        var allBlocks = blockStarts.ToHashSet();
        var dominators = new Dictionary<ulong, HashSet<ulong>>();

        foreach (var block in blockStarts)
        {
            dominators[block] = block == graph.FunctionEntry
                ? new HashSet<ulong> { block }
                : new HashSet<ulong>(allBlocks);
        }

        var changed = true;
        while (changed)
        {
            changed = false;

            foreach (var block in blockStarts.Where(block => block != graph.FunctionEntry))
            {
                var predecessors = graph.GetIncomingEdges(block)
                    .Where(IsStructuralEdge)
                    .Select(edge => edge.FromBlock)
                    .Where(dominators.ContainsKey)
                    .ToArray();

                var next = predecessors.Length == 0
                    ? new HashSet<ulong>()
                    : new HashSet<ulong>(dominators[predecessors[0]]);

                foreach (var predecessor in predecessors.Skip(1))
                {
                    next.IntersectWith(dominators[predecessor]);
                }

                next.Add(block);

                if (!next.SetEquals(dominators[block]))
                {
                    dominators[block] = next;
                    changed = true;
                }
            }
        }

        var backEdges = graph.Edges
            .Where(IsStructuralEdge)
            .Where(edge => edge.ToBlock is not null)
            .Where(edge => dominators.ContainsKey(edge.FromBlock)
                && dominators.ContainsKey(edge.ToBlock!.Value)
                && dominators[edge.FromBlock].Contains(edge.ToBlock.Value))
            .Select(edge => new BackEdge(edge.FromBlock, edge.ToBlock!.Value))
            .ToArray();

        var loops = backEdges
            .Select(edge => CreateNaturalLoop(graph, edge))
            .ToArray();

        var conditionals = FindConditionalRegions(graph, dominators);

        return new DominatorResult(dominators, backEdges, loops, conditionals);
    }

    private static NaturalLoop CreateNaturalLoop(ControlFlowGraph graph, BackEdge edge)
    {
        var loopBlocks = new HashSet<ulong> { edge.Header, edge.Source };
        var stack = new Stack<ulong>();
        stack.Push(edge.Source);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            foreach (var predecessor in graph.GetIncomingEdges(current)
                .Where(IsStructuralEdge)
                .Select(incoming => incoming.FromBlock))
            {
                if (loopBlocks.Add(predecessor))
                {
                    stack.Push(predecessor);
                }
            }
        }

        return new NaturalLoop(edge.Header, loopBlocks.Order().ToArray());
    }

    private static IReadOnlyList<ConditionalRegion> FindConditionalRegions(
        ControlFlowGraph graph,
        IReadOnlyDictionary<ulong, HashSet<ulong>> dominators)
    {
        var regions = new List<ConditionalRegion>();
        foreach (var block in graph.Blocks.Where(block => block.Kind == BasicBlockKind.Internal))
        {
            var trueEdge = graph.GetOutgoingEdges(block.StartAddress)
                .FirstOrDefault(edge => edge.Kind == ControlFlowEdgeKind.ConditionalTrue);
            var falseEdge = graph.GetOutgoingEdges(block.StartAddress)
                .FirstOrDefault(edge => edge.Kind == ControlFlowEdgeKind.ConditionalFalse);

            if (trueEdge?.ToBlock is null || falseEdge?.ToBlock is null)
            {
                continue;
            }

            var joinBlock = FindNearestCommonReachableBlock(graph, trueEdge.ToBlock.Value, falseEdge.ToBlock.Value);
            regions.Add(new ConditionalRegion(
                block.StartAddress,
                trueEdge.ToBlock.Value,
                falseEdge.ToBlock.Value,
                joinBlock));
        }

        return regions;
    }

    private static ulong? FindNearestCommonReachableBlock(ControlFlowGraph graph, ulong trueStart, ulong falseStart)
    {
        var trueReachable = TraverseStructuralSuccessors(graph, trueStart).ToHashSet();
        foreach (var candidate in TraverseStructuralSuccessors(graph, falseStart))
        {
            if (trueReachable.Contains(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<ulong> TraverseStructuralSuccessors(ControlFlowGraph graph, ulong start)
    {
        var visited = new HashSet<ulong>();
        var queue = new Queue<ulong>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!visited.Add(current))
            {
                continue;
            }

            yield return current;

            foreach (var successor in graph.GetOutgoingEdges(current)
                .Where(IsStructuralEdge)
                .Select(edge => edge.ToBlock)
                .Where(target => target is not null)
                .Select(target => target!.Value))
            {
                if (!visited.Contains(successor))
                {
                    queue.Enqueue(successor);
                }
            }
        }
    }

    private static bool IsStructuralEdge(ControlFlowEdge edge) =>
        edge.Kind is ControlFlowEdgeKind.Fallthrough
            or ControlFlowEdgeKind.ConditionalTrue
            or ControlFlowEdgeKind.ConditionalFalse
            or ControlFlowEdgeKind.Unconditional;
}

public sealed record DominatorResult(
    IReadOnlyDictionary<ulong, HashSet<ulong>> Dominators,
    IReadOnlyList<BackEdge> BackEdges,
    IReadOnlyList<NaturalLoop> NaturalLoops,
    IReadOnlyList<ConditionalRegion> ConditionalRegions);

public sealed record BackEdge(ulong Source, ulong Header);

public sealed record NaturalLoop(ulong Header, IReadOnlyList<ulong> Blocks);

public sealed record ConditionalRegion(
    ulong ConditionBlock,
    ulong TrueBlock,
    ulong FalseBlock,
    ulong? JoinBlock);
