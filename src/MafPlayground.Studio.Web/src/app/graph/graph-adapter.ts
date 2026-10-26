import { InjectionToken } from '@angular/core';
import cytoscape, { type ElementDefinition } from 'cytoscape';
import type { StudioTopology } from '../core/studio-api.generated';
import type { Invocation } from '../traces/inspection';
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
): ElementDefinition[] {
  const nodes: ElementDefinition[] = topology.nodes.map((node) => ({
    data: {
      id: 'node:' + node.id,
      actualId: node.id,
      label:
        node.label + '\n' + invocationState(invocations.filter((x) => x.executorId === node.id)),
      count: invocations.filter((x) => x.executorId === node.id).length,
    },
    classes: invocations.some((x) => x.executorId === node.id)
      ? 'observed ' +
        invocationState(invocations.filter((x) => x.executorId === node.id)).toLowerCase()
      : 'unobserved',
  }));
  for (const edge of topology.edges) {
    const id = 'edge:' + edge.id;
    if (edge.sourceIds.length === 1 && edge.targetIds.length === 1) {
      nodes.push({
        data: { id, source: 'node:' + edge.sourceIds[0], target: 'node:' + edge.targetIds[0] },
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
  return nodes;
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
          width: 140,
          height: 48,
        },
      },
      { selector: 'node.observed', style: { 'border-color': '#86d9b5' } },
      { selector: 'node.active', style: { 'border-color': '#a7afff', 'border-width': 3 } },
      { selector: 'node.failed', style: { 'border-color': '#f5a3a7' } },
      { selector: 'node.cancelled', style: { 'border-color': '#edbf7b' } },
      { selector: 'node.unknown', style: { 'border-color': '#98a4b6' } },
      { selector: 'node.unobserved', style: { 'border-style': 'dashed' } },
      {
        selector: 'node.junction',
        style: {
          shape: 'diamond',
          width: 12,
          height: 12,
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
          'curve-style': 'bezier',
        },
      },
      { selector: 'edge.conditional', style: { 'line-style': 'dashed' } },
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
        name: 'grid',
        animate: false,
        fit: !initialized,
        padding: 20,
        cols: 3,
        condense: true,
        avoidOverlap: true,
        avoidOverlapPadding: 20,
        spacingFactor: 1.1,
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
            existing.data(item.data);
            existing.classes(item.classes ?? '');
          }
        }
        cy.nodes().unselect();
        if (selected !== null) cy.getElementById('node:' + selected).select();
      });
      // Event/style updates never touch viewport or positions.
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
