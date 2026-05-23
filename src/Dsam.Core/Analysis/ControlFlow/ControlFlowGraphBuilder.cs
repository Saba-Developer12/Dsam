using Dsam.Core.Disassembly;
using Iced.Intel;

namespace Dsam.Core.Analysis.ControlFlow;

public sealed class ControlFlowGraphBuilder
{
    public ControlFlowGraph Build(ulong functionEntry, IReadOnlyList<DecodedInstruction> instructions)
    {
        var ordered = instructions
            .OrderBy(instruction => instruction.Address)
            .ToArray();

        var graph = new ControlFlowGraph(functionEntry);
        if (ordered.Length == 0)
        {
            return graph;
        }

        var instructionAddresses = ordered.Select(instruction => instruction.Address).ToHashSet();
        var leaders = DiscoverLeaders(functionEntry, ordered, instructionAddresses);
        graph.Blocks.AddRange(CreateBlocks(ordered, leaders));
        graph.RebuildIndexes();
        AddEdges(graph);
        graph.RebuildIndexes();

        return graph;
    }

    private static HashSet<ulong> DiscoverLeaders(
        ulong functionEntry,
        IReadOnlyList<DecodedInstruction> instructions,
        HashSet<ulong> instructionAddresses)
    {
        var leaders = new HashSet<ulong> { functionEntry, instructions[0].Address };

        foreach (var instruction in instructions)
        {
            var fallthrough = instruction.Address + (uint)instruction.Length;
            var flowControl = instruction.Instruction.FlowControl;

            foreach (var xref in instruction.Xrefs.Where(xref => xref.Kind == XrefKind.CodeBranch))
            {
                if (instructionAddresses.Contains(xref.ToAddress))
                {
                    leaders.Add(xref.ToAddress);
                }
            }

            if (flowControl == FlowControl.ConditionalBranch)
            {
                if (instructionAddresses.Contains(fallthrough))
                {
                    leaders.Add(fallthrough);
                }
            }
            else if (IsBlockTerminator(flowControl) && instructionAddresses.Contains(fallthrough))
            {
                leaders.Add(fallthrough);
            }
        }

        return leaders;
    }

    private static IEnumerable<BasicBlockNode> CreateBlocks(
        IReadOnlyList<DecodedInstruction> instructions,
        HashSet<ulong> leaders)
    {
        BasicBlockNode? current = null;

        foreach (var instruction in instructions)
        {
            if (current is null || leaders.Contains(instruction.Address))
            {
                if (current is not null && current.Instructions.Count > 0)
                {
                    yield return current;
                }

                current = new BasicBlockNode(instruction.Address);
            }

            current.Instructions.Add(instruction);
            current.EndAddress = instruction.Address + (uint)instruction.Length;

            if (IsBlockTerminator(instruction.Instruction.FlowControl))
            {
                yield return current;
                current = null;
            }
        }

        if (current is not null && current.Instructions.Count > 0)
        {
            yield return current;
        }
    }

    private static void AddEdges(ControlFlowGraph graph)
    {
        if (graph.Blocks.Count == 0)
        {
            return;
        }

        var minAddress = graph.Blocks
            .Where(block => block.Kind == BasicBlockKind.Internal)
            .Min(block => block.StartAddress);
        var maxAddress = graph.Blocks
            .Where(block => block.Kind == BasicBlockKind.Internal)
            .Max(block => block.EndAddress);

        for (var index = 0; index < graph.Blocks.Count; index++)
        {
            var block = graph.Blocks[index];
            if (block.Kind != BasicBlockKind.Internal)
            {
                continue;
            }

            var terminator = block.Terminator;
            if (terminator is null)
            {
                continue;
            }

            var fallthrough = block.EndAddress;
            var flowControl = terminator.Instruction.FlowControl;

            AddCallEdges(graph, block, minAddress, maxAddress);

            if (flowControl == FlowControl.ConditionalBranch)
            {
                var target = GetDirectBranchTarget(terminator);
                if (target is not null)
                {
                    graph.Edges.Add(CreateResolvedEdge(
                        graph,
                        block.StartAddress,
                        target.Value,
                        ControlFlowEdgeKind.ConditionalTrue,
                        minAddress,
                        maxAddress));
                }

                if (TryResolveInternalTarget(graph, fallthrough, out var falseBlock))
                {
                    graph.Edges.Add(new ControlFlowEdge(
                        block.StartAddress,
                        falseBlock.StartAddress,
                        ControlFlowEdgeKind.ConditionalFalse,
                        fallthrough));
                }
                else if (IsAddressOutsideRange(fallthrough, minAddress, maxAddress))
                {
                    graph.Edges.Add(CreateResolvedEdge(
                        graph,
                        block.StartAddress,
                        fallthrough,
                        ControlFlowEdgeKind.ConditionalFalse,
                        minAddress,
                        maxAddress));
                }
            }
            else if (flowControl == FlowControl.UnconditionalBranch)
            {
                var target = GetDirectBranchTarget(terminator);
                if (target is null)
                {
                    graph.Edges.Add(CreateUnresolvedEdge(
                        graph,
                        block.StartAddress,
                        ControlFlowEdgeKind.Unresolved,
                        "unresolved direct branch target"));
                }
                else
                {
                    graph.Edges.Add(CreateResolvedEdge(
                        graph,
                        block.StartAddress,
                        target.Value,
                        ControlFlowEdgeKind.Unconditional,
                        minAddress,
                        maxAddress));
                }
            }
            else if (flowControl == FlowControl.IndirectBranch)
            {
                graph.Edges.Add(CreateUnresolvedEdge(
                    graph,
                    block.StartAddress,
                    ControlFlowEdgeKind.Indirect,
                    "indirect branch"));
            }
            else if (flowControl is FlowControl.Return or FlowControl.Interrupt)
            {
                graph.Edges.Add(new ControlFlowEdge(block.StartAddress, null, ControlFlowEdgeKind.Return));
            }
            else if (flowControl == FlowControl.Exception)
            {
                graph.Edges.Add(new ControlFlowEdge(block.StartAddress, null, ControlFlowEdgeKind.Exception));
            }
            else if (TryResolveInternalTarget(graph, fallthrough, out var targetBlock))
            {
                graph.Edges.Add(new ControlFlowEdge(
                    block.StartAddress,
                    targetBlock.StartAddress,
                    ControlFlowEdgeKind.Fallthrough,
                    fallthrough));
            }
            else if (index + 1 < graph.Blocks.Count)
            {
                graph.Edges.Add(new ControlFlowEdge(
                    block.StartAddress,
                    graph.Blocks[index + 1].StartAddress,
                    ControlFlowEdgeKind.Fallthrough,
                    graph.Blocks[index + 1].StartAddress));
            }
        }
    }

    private static void AddCallEdges(
        ControlFlowGraph graph,
        BasicBlockNode block,
        ulong minAddress,
        ulong maxAddress)
    {
        foreach (var call in block.Instructions.SelectMany(instruction => instruction.Xrefs.Where(xref => xref.Kind == XrefKind.CodeCall)))
        {
            graph.Edges.Add(CreateResolvedEdge(
                graph,
                block.StartAddress,
                call.ToAddress,
                ControlFlowEdgeKind.ExternalCall,
                minAddress,
                maxAddress));
        }
    }

    private static ControlFlowEdge CreateResolvedEdge(
        ControlFlowGraph graph,
        ulong fromBlock,
        ulong targetAddress,
        ControlFlowEdgeKind kind,
        ulong minAddress,
        ulong maxAddress)
    {
        if (TryResolveInternalTarget(graph, targetAddress, out var targetBlock))
        {
            return new ControlFlowEdge(fromBlock, targetBlock.StartAddress, kind, targetAddress);
        }

        var placeholderKind = IsAddressOutsideRange(targetAddress, minAddress, maxAddress)
            ? BasicBlockKind.External
            : BasicBlockKind.Unresolved;
        var placeholder = GetOrCreatePlaceholderBlock(graph, targetAddress, placeholderKind);
        var annotation = placeholderKind == BasicBlockKind.External
            ? "target outside decoded range"
            : "target inside decoded range but not on an instruction boundary";

        return new ControlFlowEdge(fromBlock, placeholder.StartAddress, kind, targetAddress, annotation);
    }

    private static ControlFlowEdge CreateUnresolvedEdge(
        ControlFlowGraph graph,
        ulong fromBlock,
        ControlFlowEdgeKind edgeKind,
        string annotation)
    {
        var placeholderAddress = CreateSyntheticPlaceholderAddress(graph, fromBlock);
        var placeholder = GetOrCreatePlaceholderBlock(graph, placeholderAddress, BasicBlockKind.Unresolved);
        return new ControlFlowEdge(fromBlock, placeholder.StartAddress, edgeKind, null, annotation);
    }

    private static BasicBlockNode GetOrCreatePlaceholderBlock(
        ControlFlowGraph graph,
        ulong address,
        BasicBlockKind kind)
    {
        if (graph.TryGetBlock(address, out var existing))
        {
            return existing;
        }

        var block = new BasicBlockNode(address, kind)
        {
            EndAddress = address
        };
        graph.Blocks.Add(block);
        graph.RebuildIndexes();
        return block;
    }

    private static bool TryResolveInternalTarget(
        ControlFlowGraph graph,
        ulong targetAddress,
        out BasicBlockNode block)
    {
        if (graph.TryGetInstructionBlock(targetAddress, out block!))
        {
            return true;
        }

        block = default!;
        return false;
    }

    private static bool IsAddressOutsideRange(ulong address, ulong minAddress, ulong maxAddress) =>
        address < minAddress || address >= maxAddress;

    private static ulong CreateSyntheticPlaceholderAddress(ControlFlowGraph graph, ulong fromBlock)
    {
        var candidate = 0xFFFF_0000_0000_0000UL | (fromBlock & 0x0000_FFFF_FFFF_F000UL);
        while (graph.TryGetBlock(candidate, out _))
        {
            candidate++;
        }

        return candidate;
    }

    private static ulong? GetDirectBranchTarget(DecodedInstruction instruction) =>
        instruction.Xrefs
            .Where(xref => xref.Kind == XrefKind.CodeBranch)
            .Select(xref => (ulong?)xref.ToAddress)
            .FirstOrDefault();

    private static bool IsBlockTerminator(FlowControl flowControl) =>
        flowControl is FlowControl.ConditionalBranch
            or FlowControl.UnconditionalBranch
            or FlowControl.IndirectBranch
            or FlowControl.Return
            or FlowControl.Exception
            or FlowControl.Interrupt;
}
