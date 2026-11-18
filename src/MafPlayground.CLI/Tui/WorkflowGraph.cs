using MafPlayground.CLI.Inspection;
using Microsoft.Extensions.Configuration;
using Microsoft.Agents.AI.Workflows;

namespace MafPlayground.CLI.Tui;

internal sealed record GraphNode(string Id, int X, int Y);

internal sealed class WorkflowGraph
{
    public IReadOnlyList<GraphNode> Nodes { get; }
    public string[] Lines { get; }
    private readonly (string From, string To)[] _edges;

    public static WorkflowGraph For(LocalEntityDescriptor entity, IConfiguration configuration) =>
        entity.CreateWorkflowDiagram is null ? new(entity.Id, [entity.Id], []) : From(entity.CreateWorkflowDiagram(configuration));

    public static WorkflowGraph From(Workflow workflow) => new(workflow.StartExecutorId,
        workflow.ReflectExecutors().Keys, workflow.ReflectEdges().Values.SelectMany(group => group)
            .SelectMany(edge => from source in edge.Connection.SourceIds
                                from target in edge.Connection.SinkIds
                                select (source, target)));

    public IEnumerable<string> Neighbors(string id, bool forward) => forward
        ? _edges.Where(e => e.From == id).Select(e => e.To)
        : _edges.Where(e => e.To == id).Select(e => e.From);

    public WorkflowGraph(string start, IEnumerable<string> ids, IEnumerable<(string From, string To)> edges)
    {
        _edges = edges.Distinct().Order().ToArray();
        Dictionary<string, int> depth = new() { [start] = 0 };
        Queue<string> queue = new([start]);
        while (queue.TryDequeue(out string? id))
            foreach (string target in Neighbors(id, true))
                if (depth.TryAdd(target, depth[id] + 1)) queue.Enqueue(target);
        foreach (string id in ids.Order()) depth.TryAdd(id, depth.Values.Max() + 1);

        List<GraphNode> nodes = [];
        int largestLayer = depth.Values.GroupBy(level => level).Max(layer => layer.Count());
        int x = 2;
        foreach (var layer in depth.GroupBy(pair => pair.Value).OrderBy(g => g.Key))
        {
            int y = 1 + 2 * (largestLayer - layer.Count());
            foreach (var (id, _) in layer.OrderBy(pair => pair.Key))
            {
                nodes.Add(new(id, x, y));
                y += 4;
            }
            x += layer.Max(pair => pair.Key.Length) + 6;
        }
        Nodes = nodes;
        var byId = nodes.ToDictionary(n => n.Id);
        int bottom = nodes.Max(n => n.Y) + 1;
        Dictionary<(int X, int Y), int> connections = [];
        Dictionary<(int X, int Y), char> arrows = [];
        void Path(int x1, int y1, int x2, int y2)
        {
            int dx = Math.Sign(x2 - x1), dy = Math.Sign(y2 - y1);
            int outgoing = dx > 0 ? 2 : dx < 0 ? 8 : dy > 0 ? 4 : 1;
            int incoming = ((outgoing << 2) | (outgoing >> 2)) & 15;
            while (x1 != x2 || y1 != y2)
            {
                connections[(x1, y1)] = connections.GetValueOrDefault((x1, y1)) | outgoing;
                x1 += dx;
                y1 += dy;
                connections[(x1, y1)] = connections.GetValueOrDefault((x1, y1)) | incoming;
            }
        }
        foreach (var (from, to) in _edges)
        {
            var a = byId[from];
            var b = byId[to];
            int right = a.X + a.Id.Length;
            if (a.X < b.X)
            {
                int bend = b.X - 3;
                Path(right, a.Y, bend, a.Y);
                Path(bend, a.Y, bend, b.Y);
                Path(bend, b.Y, b.X - 1, b.Y);
                arrows[(b.X - 1, b.Y)] = '►';
            }
            else
            {
                // A dedicated return lane keeps cycles visible without expanding them recursively.
                int lane = a.Y == b.Y ? a.Y + 2 : ++bottom;
                bottom = Math.Max(bottom, lane);
                Path(right, a.Y, right + 1, a.Y);
                Path(right + 1, a.Y, right + 1, lane);
                Path(b.X, lane, right + 1, lane);
                Path(b.X, b.Y + 1, b.X, lane);
                arrows[(b.X, b.Y + 1)] = '▲';
            }
        }
        // Mask bits correspond clockwise to up, right, down and left.
        const string junctions = " │─└││┌├─┘─┴┐┤┬┼";
        Lines = Enumerable.Range(0, bottom + 1).Select(row =>
            new string(Enumerable.Range(0, x).Select(col => arrows.GetValueOrDefault((col, row),
                junctions[connections.GetValueOrDefault((col, row))])).ToArray())).ToArray();
    }
}
