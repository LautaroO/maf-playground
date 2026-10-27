import { InjectionToken } from '@angular/core';
import cytoscape, { type ElementDefinition } from 'cytoscape';
import type { StudioTopology } from '../core/studio-api.generated';
import type { Invocation } from '../traces/inspection';
import { topologyGeometry, NODE_WIDTH, NODE_HEIGHT, JUNCTION_SIZE } from './graph-layout';
export interface GraphDriver {
  update(elements: ElementDefinition[], selected: string | null): void;
  resize(): void;
  fit(): void;
  zoom(factor: number): void;
  destroy(): void;
}
export function invocationState(items: Invocation[]): string {
  if (!items.length) return 'Unobserved';
  if (items.some((x) => x.state === 'Active')) return 'Active';
  if (items.some((x) => x.state === 'Failed')) return 'Failed';
  if (items.some((x) => x.state === 'Cancelled')) return 'Cancelled';
  if (items.every((x) => x.state === 'Completed')) return 'Completed';
  return 'Unknown';
}
export function graphElements(
  topology: StudioTopology,
  invocations: Invocation[],
  issues: ReadonlySet<string> = new Set(),
): ElementDefinition[] {
  const nodes: ElementDefinition[] = topology.nodes.map((node) => ({
    data: {
      id: 'node:' + node.id,
      actualId: node.id,
      isStart: node.isStart || node.id === topology.startNodeId,
      label:
        (node.isStart || node.id === topology.startNodeId ? 'Entry\n' : '') +
        node.label +
        '\n' +
        invocationState(invocations.filter((x) => x.executorId === node.id)) +
        (issues.has(node.id) ? '\nIssue recorded' : ''),
      count: invocations.filter((x) => x.executorId === node.id).length,
    },
    classes:
      (issues.has(node.id) ? 'has-issue ' : '') +
      (invocations.some((x) => x.executorId === node.id)
        ? 'observed ' +
          invocationState(invocations.filter((x) => x.executorId === node.id)).toLowerCase()
        : 'unobserved'),
  }));
  for (const edge of topology.edges) {
    const id = 'edge:' + edge.id;
    if (edge.sourceIds.length === 1 && edge.targetIds.length === 1) {
      nodes.push({
        data: {
          id,
          source: 'node:' + edge.sourceIds[0],
          target: 'node:' + edge.targetIds[0],
          label: edge.label ?? '',
        },
        classes: edge.isConditional ? 'conditional' : '',
      });
      continue;
    }
    nodes.push({ data: { id, label: edge.label ?? edge.kind }, classes: 'junction' });
    for (const [index, source] of edge.sourceIds.entries())
      nodes.push({
        data: { id: `source:${edge.id}:${index}`, source: 'node:' + source, target: id },
        classes: edge.isConditional ? 'conditional' : '',
      });
    for (const [index, target] of edge.targetIds.entries())
      nodes.push({
        data: { id: `target:${edge.id}:${index}`, source: id, target: 'node:' + target },
        classes: edge.isConditional ? 'conditional' : '',
      });
  }
  const geometry = topologyGeometry(topology, nodes);
  return nodes.map((item) => {
    if (!item.data.source)
      return { ...item, position: { ...geometry.positions.get(item.data.id!)! } };
    const feedback = geometry.feedback.has(item.data.id!);
    return {
      ...item,
      data: { ...item.data, label: item.data['label'] || (feedback ? 'Return' : '') },
      classes: [item.classes, feedback ? 'feedback' : ''].filter(Boolean).join(' '),
    };
  });
}
export function createGraphDriver(
  container: HTMLElement,
  select: (id: string) => void,
): GraphDriver {
  const cy = cytoscape({
    container,
    elements: [],
    minZoom: 0.2,
    maxZoom: 3,
    style: [
      {
        selector: 'node',
        style: {
          'background-color': '#1c222d',
          'border-color': '#566379',
          'border-width': 1,
          color: '#e1e7f0',
          label: 'data(label)',
          'font-size': 12,
          'font-family': 'DM Sans, sans-serif',
          'text-wrap': 'wrap',
          'text-max-width': '120px',
          'text-valign': 'center',
          shape: 'round-rectangle',
          width: NODE_WIDTH,
          height: NODE_HEIGHT,
        },
      },
      { selector: 'node.observed', style: { 'border-color': '#86d9b5' } },
      { selector: 'node.active', style: { 'border-color': '#a7afff', 'border-width': 3 } },
      { selector: 'node.failed', style: { 'border-color': '#f5a3a7' } },
      { selector: 'node.cancelled', style: { 'border-color': '#edbf7b' } },
      { selector: 'node.unknown', style: { 'border-color': '#98a4b6' } },
      { selector: 'node.has-issue', style: { 'border-color': '#f5a3a7', 'border-width': 2 } },
      { selector: 'node.unobserved', style: { 'border-style': 'dashed' } },
      {
        selector: 'node.junction',
        style: {
          shape: 'diamond',
          width: JUNCTION_SIZE,
          height: JUNCTION_SIZE,
          label: '',
          'background-color': '#566379',
        },
      },
      {
        selector: 'edge',
        style: {
          width: 1.5,
          'line-color': '#566379',
          'target-arrow-color': '#566379',
          'target-arrow-shape': 'triangle',
          'curve-style': 'taxi',
          'taxi-direction': 'downward',
          'font-size': 10,
          label: 'data(label)',
          'text-background-color': '#11141a',
          'text-background-opacity': 1,
          'text-background-padding': '2px',
        },
      },
      { selector: 'edge.conditional', style: { 'line-style': 'dashed' } },
      {
        selector: 'edge.temporal',
        style: {
          'line-style': 'dotted',
          'line-color': '#a7afff',
          'target-arrow-color': '#a7afff',
          color: '#a7afff',
          width: 2,
          'curve-style': 'unbundled-bezier',
          'control-point-distances': [130, 130],
          'control-point-weights': [0.15, 0.85],
        },
      },
      {
        selector: 'edge.feedback',
        style: {
          'curve-style': 'unbundled-bezier',
          'control-point-distances': [100, 100],
          'control-point-weights': [0.15, 0.85],
          'line-style': 'dashed',
          'line-color': '#edbf7b',
          'target-arrow-color': '#edbf7b',
          color: '#edbf7b',
        },
      },
      {
        selector: 'node:selected',
        style: { 'border-color': '#a7afff', 'border-width': 3, 'background-color': '#252b47' },
      },
    ],
  });
  cy.on('tap', 'node', (event) => {
    const id = event.target.data('actualId');
    if (typeof id === 'string') select(id);
  });
  let initialized = false;
  let layoutPending = false;
  let positions = new Map<string, cytoscape.Position>();
  let frame: number | undefined;
  const scheduleLayout = () => {
    if (!layoutPending || frame !== undefined) return;
    frame = requestAnimationFrame(() => {
      frame = undefined;
      // Topology may arrive before Angular removes [hidden]. Never lay out a zero-sized canvas.
      if (!container.clientWidth || !container.clientHeight) return;
      layoutPending = false;
      cy.resize();
      const pan = cy.pan(),
        zoom = cy.zoom();
      cy.layout({
        name: 'preset',
        positions: Object.fromEntries(positions),
        animate: false,
        fit: !initialized,
        padding: 24,
      }).run();
      if (initialized) {
        cy.zoom(zoom);
        cy.pan(pan);
      }
      initialized = true;
    });
  };
  return {
    update(elements, selected) {
      const previousPositions = positions;
      positions = new Map(
        elements.filter((item) => item.position).map((item) => [item.data.id!, item.position!]),
      );
      const ids = new Set(elements.map((x) => x.data.id!));
      let changed = false;
      cy.batch(() => {
        cy.elements().forEach((item) => {
          if (!ids.has(item.id())) {
            item.remove();
            changed = true;
          }
        });
        for (const item of elements) {
          const existing = cy.getElementById(item.data.id!);
          if (existing.empty()) {
            cy.add(item);
            changed = true;
          } else {
            if (
              existing.isEdge() &&
              (existing.data('source') !== item.data.source ||
                existing.data('target') !== item.data.target)
            ) {
              existing.move({ source: item.data.source, target: item.data.target });
              changed = true;
            }
            if (existing.isNode() && existing.data('isStart') !== item.data['isStart'])
              changed = true;
            const previous = previousPositions.get(item.data.id!);
            if (
              existing.isNode() &&
              item.position &&
              (previous?.x !== item.position.x || previous?.y !== item.position.y)
            )
              changed = true;
            existing.data(item.data);
            existing.classes(item.classes ?? '');
          }
        }
        cy.nodes().unselect();
        if (selected !== null) cy.getElementById('node:' + selected).select();
      });
      // Unchanged geometry preserves manually moved nodes and the viewport.
      if (changed) {
        layoutPending = true;
        scheduleLayout();
      }
    },
    resize: () => {
      cy.resize();
      scheduleLayout();
    },
    fit: () => cy.fit(undefined, 30),
    zoom: (factor) =>
      cy.zoom({
        level: cy.zoom() * factor,
        renderedPosition: { x: cy.width() / 2, y: cy.height() / 2 },
      }),
    destroy: () => {
      if (frame !== undefined) cancelAnimationFrame(frame);
      cy.destroy();
    },
  };
}
export const GRAPH_DRIVER = new InjectionToken<
  (container: HTMLElement, select: (id: string) => void) => GraphDriver
>('Cytoscape graph driver', { providedIn: 'root', factory: () => createGraphDriver });
