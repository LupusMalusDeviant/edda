window.cytoscapeInterop = {
    _instances: {},
    _selectMode: {},

    init: function (container, data, dotNetRef) {
        if (typeof cytoscape === 'undefined') {
            console.warn('[cytoscapeInterop] cytoscape.js is not loaded.');
            return;
        }

        var containerId = container.id || 'cy';
        if (this._instances[containerId]) {
            this._instances[containerId].destroy();
        }

        // ── Distinct colors per leaf domain ─────────────────────────────────
        // Each subdomain gets its own vivid color. Visually distinguishable.
        var domainPalette = {
            'tools.browser':    '#38bdf8',   // sky blue
            'tools.web':        '#0ea5e9',   // ocean blue
            'tools.code':       '#22d3ee',   // cyan
            'tools.custom':     '#2dd4bf',   // teal
            'tools.knowledge':  '#60a5fa',   // cornflower
            'tools.memory':     '#818cf8',   // indigo
            'tools.multiagent': '#a78bfa',   // violet
            'tools.scheduling': '#c084fc',   // purple
            'tools.devops':     '#e879f9',   // fuchsia
            'tools.files':      '#67e8f9',   // light cyan
            'tools.security':   '#f472b6',   // pink
            'world':            '#a3e635',   // lime
            'security':         '#f87171',   // red
            'coding':           '#34d399',   // emerald
            'learnings':        '#4ade80',   // green
            'projekte':         '#c084fc',   // purple
        };
        var fallbackColors = ['#94a3b8','#64748b','#cbd5e1','#475569','#e2e8f0'];
        var fallbackIdx = 0;

        // Assign colors — use palette or generate from parent
        var domainColors = {};
        (data.nodes || []).forEach(function (n) {
            var d = n.data && n.data.domain;
            if (!d || domainColors[d]) return;
            if (domainPalette[d]) {
                domainColors[d] = domainPalette[d];
            } else {
                // Try parent domain
                var dot = d.indexOf('.');
                var parent = dot > 0 ? d.substring(0, dot) : d;
                if (domainPalette[parent]) {
                    domainColors[d] = domainPalette[parent];
                } else {
                    domainColors[d] = fallbackColors[fallbackIdx % fallbackColors.length];
                    fallbackIdx++;
                }
            }
        });

        // ── Defensive: drop dangling edges whose endpoints are not nodes ─────
        // Cytoscape throws on the first edge with a missing source/target, which
        // would abort the entire graph render. Filtering keeps the view resilient
        // regardless of what the caller supplies.
        var nodeIdSet = {};
        (data.nodes || []).forEach(function (n) {
            if (n.data && n.data.id != null) { nodeIdSet[n.data.id] = true; }
        });
        data.edges = (data.edges || []).filter(function (e) {
            return e.data && nodeIdSet[e.data.source] && nodeIdSet[e.data.target];
        });

        // ── Pre-compute degree for node sizing ──────────────────────────────
        var degreeMap = {};
        data.edges.forEach(function (e) {
            var s = e.data.source, t = e.data.target;
            degreeMap[s] = (degreeMap[s] || 0) + 1;
            degreeMap[t] = (degreeMap[t] || 0) + 1;
        });

        // ── Build short label from ID ───────────────────────────────────────
        function shortLabel(id) {
            if (!id) return '';
            // Git hierarchy nodes: show a clean name; the git origin is already clear from the edges.
            if (id === 'git-knowledge') { return 'Repositories'; }
            if (id === 'uploads') { return 'Uploads'; }
            if (id.indexOf('upload:') === 0) {
                var urest = id.substring(7);
                var uc = urest.indexOf(':');
                return uc >= 0 ? urest.substring(uc + 1) : urest;  // file -> path, source -> slug
            }
            if (id.indexOf('git-host:') === 0) {
                var h = id.substring(9);
                return h.indexOf('git.') === 0 ? h.substring(4) : h;  // drop redundant "git." subdomain
            }
            if (id.indexOf('git-group:') === 0) {
                var gp = id.substring(10).split('/');
                return gp[gp.length - 1];
            }
            if (id.indexOf('git:') === 0) {
                var rest = id.substring(4);
                var c = rest.indexOf(':');
                return c >= 0 ? rest.substring(c + 1) : rest;  // file -> path, repo -> slug
            }
            var parts = id.split('-');
            var prefixes = ['tool','world','rule','coding','security','learning'];
            if (parts.length > 1 && prefixes.indexOf(parts[0]) >= 0) {
                parts = parts.slice(1);
            }
            var label = parts.join('-');
            return label.length > 22 ? label.substring(0, 20) + '…' : label;
        }

        // ── Build domain label ──────────────────────────────────────────────
        function domainLabel(d) {
            var dot = d.indexOf('.');
            return dot > 0 ? d.substring(dot + 1).toUpperCase() : d.toUpperCase();
        }

        // ── Flat rule nodes (NO compounds) ──────────────────────────────────
        var flatRuleNodes = (data.nodes || []).map(function (n) {
            var d = Object.assign({}, n.data);
            d.label = shortLabel(d.id);
            d.degree = degreeMap[d.id] || 0;
            d.domainLabel = domainLabel(d.domain);
            return { data: d };
        });

        // ── Edge colours ────────────────────────────────────────────────────
        var edgeColors = {
            implies:    '#22c55e',
            conflicts:  '#ef4444',
            exception:  '#f59e0b',
            requires:   '#475569',
            supersedes: '#a855f7',
            related:    '#6366f1'
        };

        // ── Node size: base 36, scales with degree ──────────────────────────
        function nodeSize(ele) {
            var deg = ele.data('degree') || 0;
            return Math.max(36, Math.min(80, 36 + deg * 6));
        }

        var style = [
            // ── Rule nodes ──────────────────────────────────────────────────
            {
                selector: 'node',
                style: {
                    'background-color': function (e) { return domainColors[e.data('domain')] || '#64748b'; },
                    'label':            'data(label)',
                    'color':            '#e2e8f0',
                    'font-size':        '10px',
                    'text-valign':      'bottom',
                    'text-halign':      'center',
                    'text-margin-y':    '5px',
                    'text-wrap':        'wrap',
                    'text-max-width':   '110px',
                    'text-outline-color': '#0f172a',
                    'text-outline-width': '2px',
                    'text-outline-opacity': 0.9,
                    'width':            nodeSize,
                    'height':           nodeSize,
                    'shape':            'ellipse',
                    'border-width':     '2px',
                    'border-color':     function (e) {
                        var c = domainColors[e.data('domain')] || '#64748b';
                        return c + '88';  // semi-transparent version of domain color
                    },
                    'transition-property': 'background-color, border-color, border-width, width, height, opacity',
                    'transition-duration': '0.2s',
                    'min-zoomed-font-size': 7
                }
            },
            // ── Non-rule node kinds: tool / world / external ─────────
            {
                selector: 'node[kind="tool"]',
                style: { 'shape': 'round-rectangle', 'background-color': '#14b8a6', 'border-color': '#0f766e' }
            },
            {
                selector: 'node[kind="world"]',
                style: { 'shape': 'diamond', 'background-color': '#0ea5e9', 'border-color': '#0369a1' }
            },
            {
                selector: 'node[kind="external"]',
                style: { 'shape': 'hexagon', 'background-color': '#64748b', 'border-color': '#334155' }
            },
            // ── Hover state ─────────────────────────────────────────────────
            {
                selector: 'node.hover',
                style: {
                    'border-color':     '#ffffff',
                    'border-width':     '3px',
                    'z-index':          999
                }
            },
            // ── Selected state ──────────────────────────────────────────────
            {
                selector: 'node:selected',
                style: {
                    'background-color': '#f59e0b',
                    'border-width':     '3px',
                    'border-color':     '#ffffff',
                    'width':            function (e) { return nodeSize(e) + 14; },
                    'height':           function (e) { return nodeSize(e) + 14; },
                    'font-size':        '12px',
                    'font-weight':      'bold',
                    'text-outline-width': '3px',
                    'z-index':          1000
                }
            },
            // ── Edges ───────────────────────────────────────────────────────
            {
                selector: 'edge',
                style: {
                    'line-color':          function (e) { return edgeColors[e.data('type')] || '#475569'; },
                    'target-arrow-color':  function (e) { return edgeColors[e.data('type')] || '#475569'; },
                    'target-arrow-shape':  'triangle',
                    'arrow-scale':         0.7,
                    'curve-style':         'bezier',
                    'width':               1.2,
                    'opacity':             0.35,
                    'label':               '',
                    'font-size':           '8px',
                    'color':               '#94a3b8',
                    'text-rotation':       'autorotate',
                    'text-margin-y':       -8
                }
            },
            {
                selector: 'edge.hover',
                style: {
                    'label':    function (e) { return e.data('type') || ''; },
                    'opacity':  0.9,
                    'width':    2.5
                }
            },
            {
                selector: 'edge[type="conflicts"]',
                style: { 'line-style': 'dashed', 'target-arrow-shape': 'tee' }
            },
            // ── Dimmed state ────────────────────────────────────────────────
            {
                selector: '.dimmed',
                style: { 'opacity': 0.08 }
            },
            // ── Highlighted neighbors ───────────────────────────────────────
            {
                selector: '.highlighted',
                style: { 'opacity': 1 }
            }
        ];

        // ── Create graph — no compounds, pure flat layout ───────────────────
        var cy = cytoscape({
            container: container,
            elements:  [...flatRuleNodes, ...(data.edges || [])],
            style:     style,
            layout:    { name: 'preset' },
            minZoom:   0.1,
            maxZoom:   4,
            wheelSensitivity: 0.25
        });

        // ── Run CoSE layout ─────────────────────────────────────────────────
        cy.layout({
            name:              'cose',
            animate:           false,
            padding:           50,
            nodeRepulsion:     function () { return 15000; },
            idealEdgeLength:   function () { return 140; },
            edgeElasticity:    function () { return 80; },
            componentSpacing:  120,
            gravity:           0.12,
            numIter:           2000,
            initialTemp:       1000,
            coolingFactor:     0.99,
            minTemp:           1.0,
            fit:               true,
            randomize:         true
        }).run();

        // ── Interaction: hover ──────────────────────────────────────────────
        cy.on('mouseover', 'node', function (evt) {
            evt.target.addClass('hover');
            evt.target.connectedEdges().addClass('hover');
        });
        cy.on('mouseout', 'node', function (evt) {
            evt.target.removeClass('hover');
            evt.target.connectedEdges().removeClass('hover');
        });
        cy.on('mouseover', 'edge', function (evt) { evt.target.addClass('hover'); });
        cy.on('mouseout', 'edge', function (evt) { evt.target.removeClass('hover'); });

        // ── Interaction: click to focus neighborhood ────────────────────────
        cy.on('tap', 'node', function (evt) {
            // In select mode, clicking toggles selection natively — skip the focus/detail behavior.
            if (window.cytoscapeInterop._selectMode[containerId]) { return; }
            var node = evt.target;
            var hood = node.closedNeighborhood();

            cy.elements().addClass('dimmed').removeClass('highlighted');
            hood.removeClass('dimmed').addClass('highlighted');
            hood.connectedEdges().removeClass('dimmed').addClass('highlighted');

            if (dotNetRef) {
                dotNetRef.invokeMethodAsync('OnNodeClicked', node.data().id);
            }
        });

        // ── Click background: reset ─────────────────────────────────────────
        cy.on('tap', function (evt) {
            if (evt.target === cy) {
                cy.elements().removeClass('dimmed highlighted');
            }
        });

        // ── Selection changes → notify .NET for a live count (fires only in select mode) ───
        cy.on('select unselect', 'node', function () {
            if (dotNetRef) {
                dotNetRef.invokeMethodAsync('OnSelectionChanged', cy.nodes(':selected').length);
            }
        });

        // Start in view mode: panning on, box-selection + node selection off.
        this._selectMode[containerId] = false;
        cy.userPanningEnabled(true);
        cy.boxSelectionEnabled(false);
        cy.autounselectify(true);

        this._instances[containerId] = cy;
    },

    // ── Multi-select mode ───────────────────────────────────────────────────
    // Toggles between pan mode (drag = pan) and select mode (drag = rubber-band box select,
    // click = toggle node). Driven by the "Mehrere Knoten bearbeiten" button in the UI.
    setSelectMode: function (containerId, enabled) {
        var cy = this._instances[containerId];
        if (!cy) { return; }
        this._selectMode[containerId] = enabled;
        if (enabled) {
            cy.autounselectify(false);
            cy.selectionType('additive');
            cy.boxSelectionEnabled(true);
            cy.userPanningEnabled(false);
            cy.elements().removeClass('dimmed highlighted');
        } else {
            cy.$(':selected').unselect();
            cy.boxSelectionEnabled(false);
            cy.userPanningEnabled(true);
            cy.autounselectify(true);
        }
    },

    // Returns the ids of the currently selected nodes.
    getSelectedIds: function (containerId) {
        var cy = this._instances[containerId];
        if (!cy) { return []; }
        return cy.nodes(':selected').map(function (n) { return n.id(); });
    }
};
