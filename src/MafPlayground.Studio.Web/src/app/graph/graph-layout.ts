import { Graph, layout, type GraphLabel, type NodeLabel, type EdgeLabel } from '@dagrejs/dagre';
import type { ElementDefinition, Position } from 'cytoscape';
import type { StudioTopology } from '../core/studio-api.generated';

export const NODE_WIDTH = 140;
export const NODE_HEIGHT = 56;
export const JUNCTION_SIZE = 12;
interface Geometry {
  positions: Map<string, Position>;
  feedback: Set<string>;
}
// Declarations are immutable snapshots. Lifecycle updates reuse their geometry;
// evicted declarations and their cached layout can be garbage-collected together.
const layouts = new WeakMap<StudioTopology, Geometry>();
export function topologyGeometry(
  topology: StudioTopology,
  elements: ElementDefinition[],
): Geometry {
  const cached = layouts.get(topology);
  if (cached) return cached;
  const positions = new Map<string, Position>(),
    feedback = new Set<string>();
  if (elements.length) {
    const graph = new Graph<GraphLabel, NodeLabel, EdgeLabel>({ multigraph: true });
    graph.setGraph({
      rankdir: 'TB',
      ranksep: 38,
      nodesep: 42,
      edgesep: 18,
      marginx: 24,
      marginy: 24,
    });
    const nodes = elements
      .filter((item) => !item.data.source)
      .sort(
        (a, b) =>
          Number(b.data['isStart'] === true) - Number(a.data['isStart'] === true) ||
          a.data.id!.localeCompare(b.data.id!),
      );
    for (const item of nodes) {
      const junction = String(item.classes ?? '').includes('junction');
      graph.setNode(item.data.id!, {
        width: junction ? JUNCTION_SIZE : NODE_WIDTH,
        height: junction ? JUNCTION_SIZE : NODE_HEIGHT,
      });
    }
    const edges = elements
      .filter((item) => item.data.source)
      .sort((a, b) => a.data.id!.localeCompare(b.data.id!));
    for (const item of edges)
      graph.setEdge(item.data.source!, item.data.target!, {}, item.data.id!);
    layout(graph);
    for (const id of graph.nodes()) {
      const node = graph.node(id);
      positions.set(id, { x: node.x!, y: node.y! });
    }
    for (const item of edges) {
      if (positions.get(item.data.target!)!.y <= positions.get(item.data.source!)!.y)
        feedback.add(item.data.id!);
    }
  }
  const result = { positions, feedback };
  layouts.set(topology, result);
  return result;
}
