// The app's file (TM Concept 0.1.33): draws every scene from its plan (input.scenes[i].plan). No model writes this file; the model only
// writes the plan (what is on screen, where and when), so the same drawer serves every topic. Loaded after gsap, window.INPUT, tm.js and
// (for map scenes) world.js. Everything is a function of the paused timeline's time: no timers, no unseeded randomness, frames can be sought.
(function () {
  "use strict";
  var input = window.INPUT;
  var TM = window.TM;
  var root = document.getElementById("root");
  var SVG = "http://www.w3.org/2000/svg";
  var W = input.width;
  var H = input.height;
  var unit = Math.min(W, H);
  var style = input.style || {};
  var motion = style.motion || "normal";
  var calm = motion === "calm";
  var lively = motion === "lively";
  var crafts = { "paper-cut": "slide", "terminal": "type", "flat": "pop", "cartoon": "pop", "infographic": "pop", "isometric": "rise" };
  var craft = crafts[style.drawStyle] || "draw";
  var glow = ["neon", "hologram", "terminal"].indexOf(style.drawStyle) >= 0;
  var C = {
    accent: TM.color("accent"), accent2: TM.color("accent2"), text: TM.color("text"), background: TM.color("background"),
    surface: TM.color("surface"), muted: TM.color("muted")
  };
  var upper = TM.margin;
  var lower = H - TM.lower;
  var stroke = Math.max(3, Math.round(unit * 0.006));
  var tl = gsap.timeline({ paused: true });
  var counter = 0;

  function el(tag, holder, css) {
    var e = document.createElement(tag);
    if (css) e.style.cssText = css;
    if (holder) holder.appendChild(e);
    return e;
  }

  function sv(tag, holder, attrs) {
    var e = document.createElementNS(SVG, tag);
    for (var k in attrs) if (Object.prototype.hasOwnProperty.call(attrs, k)) e.setAttribute(k, String(attrs[k]));
    if (holder) holder.appendChild(e);
    return e;
  }

  function uid(prefix) { counter += 1; return "tm" + prefix + counter; }

  // Entrance lengths stay within the motion rules: a pop at most 0.45 s, a fade 0.8 s, a slide 0.9 s; calm is slower and quieter.
  function enter(target, at) {
    var d = calm ? 0.8 : 0.45;
    if (craft === "slide") tl.fromTo(target, { opacity: 0, x: -unit * 0.06 }, { opacity: 1, x: 0, duration: calm ? 0.9 : 0.6, ease: "power2.out" }, at);
    else if (craft === "pop" && !calm) tl.fromTo(target, { opacity: 0, scale: 0.6, transformOrigin: "50% 50%" }, { opacity: 1, scale: 1, duration: d, ease: "back.out(2)" }, at);
    else if (craft === "rise") tl.fromTo(target, { opacity: 0, y: unit * 0.03 }, { opacity: 1, y: 0, duration: d, ease: "power2.out" }, at);
    else tl.fromTo(target, { opacity: 0 }, { opacity: 1, duration: d, ease: "power1.out" }, at);
  }

  // A text that appears in the style's way (typed on a terminal, otherwise entering like everything else).
  function textIn(node, words, at) {
    if (craft !== "type" || calm) {
      node.textContent = words;
      enter(node, at);
      return;
    }

    var state = { n: 0 };
    node.textContent = "";
    tl.set(node, { opacity: 1 }, at);
    tl.to(state, {
      n: words.length, duration: Math.min(2.5, words.length / 13), ease: "none",
      onUpdate: function () { node.textContent = words.slice(0, Math.round(state.n)); }
    }, at);
  }

  // Map scenes: an equirectangular view of the region fitted into the safe area (longitudes shrunk by the cosine of the middle latitude).
  function projector(region) {
    var w = region[0], s = region[1], e = region[2], n = region[3];
    var kx = Math.cos(((s + n) / 2) * Math.PI / 180);
    var boxW = W - 2 * TM.margin, boxH = lower - upper;
    var scale = Math.min(boxW / Math.max(0.1, (e - w) * kx), boxH / Math.max(0.1, n - s));
    var ox = (W - (e - w) * kx * scale) / 2, oy = upper + (boxH - (n - s) * scale) / 2;
    return function (lon, lat) { return [ox + (lon - w) * kx * scale, oy + (n - lat) * scale]; };
  }

  function drawMap(svg, plan) {
    var world = window.TM_WORLD;
    var shapes = {};
    if (!world || !plan.map) return shapes;
    var project = projector(plan.map.region);
    var k = world.scale || 10;
    world.countries.forEach(function (country) {
      var d = "";
      country.r.forEach(function (ring) {
        var lon = ring[0] / k, lat = ring[1] / k;
        var p = project(lon, lat);
        d += "M" + p[0].toFixed(1) + " " + p[1].toFixed(1);
        for (var i = 2; i < ring.length; i += 2) {
          lon += ring[i] / k;
          lat += ring[i + 1] / k;
          p = project(lon, lat);
          d += "L" + p[0].toFixed(1) + " " + p[1].toFixed(1);
        }
        d += "Z";
      });
      shapes[country.c] = sv("path", svg, { d: d, fill: C.surface, stroke: C.muted, "stroke-width": Math.max(1, stroke / 3), "stroke-linejoin": "round" });
    });
    return shapes;
  }

  function drawDiagram(svg, layer, plan) {
    var size = unit * 0.07;
    (plan.links || []).forEach(function (link) {
      var a = node(plan, link.from), b = node(plan, link.to);
      if (a && b) sv("line", svg, { x1: a[0], y1: a[1], x2: b[0], y2: b[1], stroke: C.muted, "stroke-width": stroke });
    });
    (plan.nodes || []).forEach(function (n) {
      var x = n.x * W, y = n.y * H;
      var w = Math.max(size * 2.4, (n.label || "").length * size * 0.32);
      sv("rect", svg, { x: x - w / 2, y: y - size / 2, width: w, height: size, rx: size * 0.2, fill: C.surface, stroke: C.accent, "stroke-width": stroke });
      var t = el("div", layer, "position:absolute;text-align:center;font-weight:700;white-space:nowrap;color:" + C.text + ";font-size:" + Math.round(size * 0.36) + "px;");
      t.textContent = n.label || "";
      t.style.left = Math.round(x) + "px";
      t.style.setProperty("top", Math.round(y) + "px");
      t.style.transform = "translate(-50%, -50%)";
    });
  }

  function node(plan, id) {
    var n = (plan.nodes || []).filter(function (x) { return x.id === id; })[0];
    return n ? [n.x * W, n.y * H] : null;
  }

  // Where a named point is on screen: a map anchor by longitude and latitude, an image anchor by its fractions, a diagram node.
  function point(plan, project, id) {
    var a = (plan.anchors || []).filter(function (x) { return x.id === id; })[0];
    if (a) {
      if (project && typeof a.lon === "number" && typeof a.lat === "number") return project(a.lon, a.lat);
      if (typeof a.x === "number" && typeof a.y === "number") return [a.x * W, a.y * H];
    }

    return node(plan, id);
  }

  function label(plan, id) {
    var a = (plan.anchors || []).filter(function (x) { return x.id === id; })[0];
    return a && a.label ? a.label : "";
  }

  function pill(layer, words, p, at) {
    var size = Math.round(unit * 0.034);
    var box = el("div", layer, "position:absolute;padding:0.2em 0.55em;border-radius:0.4em;font-weight:700;white-space:nowrap;opacity:0;"
      + "font-size:" + size + "px;color:" + C.text + ";background:" + C.background + "cc;border:" + Math.max(2, stroke / 2) + "px solid " + C.accent + ";");
    box.style.left = Math.round(p[0]) + "px";
    box.style.setProperty("top", Math.round(Math.min(lower - size * 2, Math.max(upper, p[1] - size * 1.6))) + "px");
    box.style.transform = "translate(-50%, -100%)";
    textIn(box, words, at);
    return box;
  }

  function smooth(points) {
    if (points.length < 3) return "M" + points.map(function (p) { return p[0].toFixed(1) + " " + p[1].toFixed(1); }).join(" L");
    var d = "M" + points[0][0].toFixed(1) + " " + points[0][1].toFixed(1);
    for (var i = 1; i < points.length - 1; i++) {
      var mx = (points[i][0] + points[i + 1][0]) / 2, my = (points[i][1] + points[i + 1][1]) / 2;
      d += " Q" + points[i][0].toFixed(1) + " " + points[i][1].toFixed(1) + " " + mx.toFixed(1) + " " + my.toFixed(1);
    }
    var last = points[points.length - 1];
    return d + " L" + last[0].toFixed(1) + " " + last[1].toFixed(1);
  }

  function arrowHead(svg) {
    var id = uid("a");
    var defs = sv("defs", svg, {});
    var marker = sv("marker", defs, { id: id, viewBox: "0 0 10 10", refX: 7, refY: 5, markerWidth: 4, markerHeight: 4, orient: "auto-start-reverse" });
    sv("path", marker, { d: "M0 0 L10 5 L0 10 Z", fill: C.accent });
    return "url(#" + id + ")";
  }

  function icon(kind) {
    if (kind === "person" || kind === "army") {
      var figure = TM.figure("walk");
      figure.style.height = Math.round(unit * (kind === "army" ? 0.09 : 0.075)) + "px";
      figure.setAttribute("stroke", C.accent);
      return figure;
    }

    var g = document.createElementNS(SVG, "svg");
    g.setAttribute("viewBox", "0 0 100 100");
    g.style.position = "absolute";
    g.style.height = Math.round(unit * 0.07) + "px";
    var shapes = {
      ship: "M10 62 L90 62 L76 82 L24 82 Z M50 10 L50 60 M50 14 L78 54 L50 54 Z",
      caravan: "M14 70 L86 70 L86 44 L14 44 Z M28 84 A8 8 0 1 0 28.1 84 M72 84 A8 8 0 1 0 72.1 84 M14 44 L30 28 L70 28 L86 44",
      rider: "M20 70 L80 70 M30 70 L30 50 L70 50 L70 70 M70 50 L82 38 M44 50 L50 26 M50 18 A6 6 0 1 0 50.1 18"
    };
    var p = sv("path", g, { d: shapes[kind] || "M50 30 A20 20 0 1 0 50.1 30", fill: kind === "ship" || !shapes[kind] ? C.accent : "none",
      stroke: C.accent, "stroke-width": 7, "stroke-linejoin": "round", "stroke-linecap": "round" });
    if (glow) p.style.filter = "drop-shadow(0 0 6px " + C.accent + ")";
    return g;
  }

  // One action of a beat at time "at" for "span" seconds.
  function act(a, ctx, at, span) {
    var plan = ctx.plan, svg = ctx.svg, layer = ctx.overlay, project = ctx.project;
    var draw = calm ? 0.8 : Math.max(0.6, Math.min(2.5, span * 0.8));
    var start = calm ? ctx.sceneStart : at;
    if (a.type === "path" || a.type === "arrow") {
      var ids = [a.from].concat(a.via || []).concat([a.to]);
      var pts = ids.map(function (id) { return point(plan, project, id); }).filter(Boolean);
      if (pts.length < 2) return;
      var path = sv("path", svg, { d: a.type === "arrow" ? smooth([pts[0], pts[pts.length - 1]]) : smooth(pts), fill: "none", stroke: C.accent,
        "stroke-width": stroke * (a.type === "arrow" ? 1.4 : 1.2), "stroke-linecap": "round", "stroke-linejoin": "round", pathLength: 1,
        "marker-end": arrowHead(svg) });
      if (glow) path.style.filter = "drop-shadow(0 0 8px " + C.accent + ")";
      if (a.dashed) path.setAttribute("stroke-dasharray", "0.02 0.015");
      else {
        path.style.strokeDasharray = "1";
        path.style.strokeDashoffset = calm ? "0" : "1";
      }

      if (calm || a.dashed) enter(path, start);
      else tl.to(path, { strokeDashoffset: 0, duration: draw, ease: "power1.inOut" }, start);
      var walker = a.walker || "";
      if (walker && lively) {
        var w = icon(walker);
        layer.appendChild(w);
        var state = { t: 0 };
        var place = function () {
          var length = path.getTotalLength();
          var q = path.getPointAtLength(length * state.t);
          w.style.left = Math.round(q.x) + "px";
          w.style.setProperty("top", Math.round(q.y) + "px");
          w.style.transform = "translate(-50%, -85%)";
        };
        place();
        tl.fromTo(w, { opacity: 0 }, { opacity: 1, duration: 0.3 }, start);
        tl.to(state, { t: 1, duration: Math.max(1, span - 0.3), ease: "sine.inOut", onUpdate: place }, start);
      } else if (walker && calm) {
        var still = icon(walker);
        layer.appendChild(still);
        var end = pts[pts.length - 1];
        still.style.left = Math.round(end[0]) + "px";
        still.style.setProperty("top", Math.round(end[1]) + "px");
        still.style.transform = "translate(-50%, -85%)";
        enter(still, start);
      }

      if (a.label) pill(layer, a.label, pts[Math.floor(pts.length / 2)], start + (calm ? 0 : draw * 0.5));
    } else if (a.type === "pin") {
      var p = point(plan, project, a.at);
      if (!p) return;
      var r = unit * 0.014;
      var dot = sv("circle", svg, { cx: p[0], cy: p[1], r: r, fill: C.accent, stroke: C.background, "stroke-width": Math.max(2, stroke / 2) });
      enter(dot, start);
      if (!calm) {
        var ring = sv("circle", svg, { cx: p[0], cy: p[1], r: r, fill: "none", stroke: C.accent, "stroke-width": Math.max(2, stroke / 2) });
        tl.fromTo(ring, { attr: { r: r }, opacity: 0.9 }, { attr: { r: r * 3.5 }, opacity: 0, duration: 1.2, ease: "power1.out", repeat: lively ? 2 : 0 }, start);
      }

      var words = a.label || label(plan, a.at);
      if (words) pill(layer, words, p, start + (calm ? 0 : 0.15));
    } else if (a.type === "label") {
      var lp = point(plan, project, a.at);
      if (lp) pill(layer, a.text || label(plan, a.at), lp, start);
    } else if (a.type === "highlight") {
      var hp = point(plan, project, a.at);
      if (!hp) return;
      var halo = sv("circle", svg, { cx: hp[0], cy: hp[1], r: unit * (a.size || 0.08), fill: C.accent + "22", stroke: C.accent, "stroke-width": stroke });
      if (glow) halo.style.filter = "drop-shadow(0 0 10px " + C.accent + ")";
      enter(halo, start);
      if (lively) tl.to(halo, { opacity: 0.55, duration: 0.6, yoyo: true, repeat: 3, ease: "sine.inOut" }, start + 0.5);
    } else if (a.type === "country") {
      var shape = ctx.countries[a.country];
      if (shape) tl.to(shape, { attr: { fill: C.accent2 }, duration: calm ? 0.01 : 0.6, ease: "power1.out" }, start);
      if (a.label && shape) {
        var box = shape.getBBox();
        pill(layer, a.label, [box.x + box.width / 2, box.y + box.height / 2], start);
      }
    } else if (a.type === "counter") {
      var cp = point(plan, project, a.at) || [W / 2, upper + (lower - upper) * 0.2];
      var size = Math.round(unit * 0.11);
      var number = el("div", layer, "position:absolute;font-weight:800;white-space:nowrap;opacity:0;color:" + C.accent + ";font-size:" + size + "px;");
      number.style.left = Math.round(cp[0]) + "px";
      number.style.setProperty("top", Math.round(cp[1]) + "px");
      number.style.transform = "translate(-50%, -50%)";
      var from = typeof a.fromValue === "number" ? a.fromValue : 0, to = typeof a.value === "number" ? a.value : from;
      var suffix = a.suffix || "";
      var value = { v: calm ? to : from };
      // Years and small numbers without a thousands mark (1453, not 1.453); large amounts grouped in the video's language.
      var grouped = Math.abs(to) >= 10000 || Math.abs(from) >= 10000;
      var show = function () {
        var v = Math.round(value.v);
        number.textContent = (grouped ? v.toLocaleString(input.language) : String(v)) + suffix;
      };
      show();
      enter(number, start);
      if (!calm) tl.to(value, { v: to, duration: Math.max(0.8, Math.min(2.5, span * 0.7)), ease: "power1.inOut", onUpdate: show }, start);
    } else if (a.type === "text") {
      var title = TM.title(a.text || "");
      layer.appendChild(title);
      enter(title, start);
    }
  }

  // The camera: a zoom on a named point, centred in the safe area, never showing past the picture's edges.
  function cameraTo(ctx, cam, at, span) {
    if (!cam) return;
    var z = Math.max(1, Math.min(2.2, cam.zoom || 1));
    var p = cam.focus ? point(ctx.plan, ctx.project, cam.focus) : null;
    var cx = p ? p[0] : W / 2, cy = p ? p[1] : H / 2;
    var x = Math.min(0, Math.max(W - W * z, W / 2 - cx * z));
    var y = Math.min(0, Math.max(H - H * z, (upper + lower) / 2 - cy * z));
    var d = calm ? Math.max(1.5, span) : Math.max(0.8, Math.min(1.6, span * 0.5));
    tl.to(ctx.camera, { x: x, y: y, scale: z, duration: d, ease: cam.ease || "power2.inOut" }, at);
  }

  // The way a scene comes in (0.1.42, plan.transition, settled by the app): the new layer over the old one, which is hidden once it is
  // covered. Directions are where the picture moves ("left": the new scene comes from the right). Without a transition (a plan made before,
  // the first scene, a placeholder of the text) the scene appears at once, as it always did.
  var shifts = { left: [1, 0], right: [-1, 0], up: [0, 1], down: [0, -1] };
  var wipes = { left: "inset(0% 0% 0% 100%)", right: "inset(0% 100% 0% 0%)", up: "inset(100% 0% 0% 0%)", down: "inset(0% 0% 100% 0%)" };

  function transitionIn(layer, previous, scene, plan, start) {
    var name = plan.transition || "";
    var d = plan.transitionSeconds || 0;
    var dash = name.lastIndexOf("-");
    var family = name === "blur-dissolve" || name === "zoom-through" || dash < 0 ? name : name.slice(0, dash);
    var way = shifts[name.slice(dash + 1)] ? name.slice(dash + 1) : "left";
    var v = shifts[way];
    if (!previous || scene.transition || d <= 0 || ["crossfade", "blur-dissolve", "slide", "push", "wipe", "zoom-through"].indexOf(family) < 0) {
      tl.set(layer, { opacity: 1 }, start);
      return;
    }

    if (family === "crossfade") {
      tl.fromTo(layer, { opacity: 0 }, { opacity: 1, duration: d, ease: "power2.inOut", immediateRender: false }, start);
    } else if (family === "blur-dissolve") {
      tl.fromTo(previous, { filter: "blur(0px)" }, { filter: "blur(10px)", duration: d, ease: "power2.inOut", immediateRender: false }, start);
      tl.fromTo(layer, { opacity: 0, filter: "blur(10px)" }, { opacity: 1, filter: "blur(0px)", duration: d, ease: "power2.inOut", immediateRender: false }, start);
    } else if (family === "zoom-through") {
      // Out 1 -> 1.2 on power3.in before the start, in 0.75 -> 1 on expo.out after it, never more than 10 px of blur.
      var half = d / 2;
      tl.fromTo(previous, { scale: 1, filter: "blur(0px)", transformOrigin: "50% 50%" },
        { scale: 1.2, filter: "blur(10px)", duration: half, ease: "power3.in", transformOrigin: "50% 50%", immediateRender: false }, Math.max(0, start - half));
      tl.fromTo(layer, { opacity: 0, scale: 0.75, filter: "blur(10px)", transformOrigin: "50% 50%" },
        { opacity: 1, scale: 1, filter: "blur(0px)", duration: half, ease: "expo.out", transformOrigin: "50% 50%", immediateRender: false }, start);
      d = half;
    } else if (family === "wipe") {
      tl.set(layer, { opacity: 1 }, start);
      tl.fromTo(layer, { clipPath: wipes[way] }, { clipPath: "inset(0% 0% 0% 0%)", duration: d, ease: "power2.inOut", immediateRender: false }, start);
    } else {
      tl.set(layer, { opacity: 1 }, start);
      tl.fromTo(layer, { x: v[0] * W, y: v[1] * H }, { x: 0, y: 0, duration: d, ease: "power3.inOut", immediateRender: false }, start);
      if (family === "push") {
        tl.fromTo(previous, { x: 0, y: 0 }, { x: -v[0] * W, y: -v[1] * H, duration: d, ease: "power3.inOut", immediateRender: false }, start);
      }
    }

    tl.set(previous, { opacity: 0 }, start + d);
  }

  function presenter(ctx, beats, sceneStart, sceneEnd) {
    if (style.drawStyle !== "stick-figure") return;
    var poses = {};
    var height = Math.round(H * 0.24);
    var spot = el("div", ctx.layer, "position:absolute;left:" + Math.round(TM.margin * 0.6) + "px;top:" + Math.round(lower - height) + "px;width:"
      + Math.round(height * 0.5) + "px;height:" + height + "px;");
    ["stand", "point", "think", "walk", "cheer"].forEach(function (name) {
      var f = TM.figure(name);
      f.style.height = height + "px";
      f.style.left = "0px";
      f.style.setProperty("top", "0px");
      f.style.opacity = "0";
      spot.appendChild(f);
      poses[name] = f;
    });
    var current = null;
    function pose(name, at) {
      if (current) tl.set(current, { opacity: 0 }, at);
      tl.set(poses[name], { opacity: 1 }, at);
      current = poses[name];
    }
    pose("stand", sceneStart);
    beats.forEach(function (b, i) {
      var acts = (b.actions || []).map(function (a) { return a.type; });
      var name = acts.indexOf("pin") >= 0 || acts.indexOf("highlight") >= 0 || acts.indexOf("label") >= 0 ? "point"
        : acts.indexOf("path") >= 0 ? (lively ? "walk" : "point") : acts.indexOf("counter") >= 0 ? "cheer" : (i % 2 ? "think" : "stand");
      pose(name, b.start);
    });
    tl.set(spot, {}, sceneEnd);
  }

  // F12 (0.1.43): the finishing layer, only when the template has it on (input.style.finish; absent = nothing below runs, no canvas, the video
  // as before). One canvas over every scene layer, painted from the timeline's time by a tween of the timeline itself (no timer, no other
  // process), so a frame is the same however and in whatever order it is reached. Every random choice comes from rnd(key), a stream seeded
  // by finish.seed and the key (FNV-1a + mulberry32): the same key gives the same numbers in every frame. The paper tooth, the vignette and
  // the glow are made once; the grain and the boil change 12 times a second. Shadow numbers by depth follow ReelMimic's paper-cutout engine
  // (MIT): opacity 0.2 + 0.09 s (at most 0.6), offset 1.5 + 4.5 s and blur 2 + 6 s at 1080 px, light from the top left.
  var finish = style.finish || null;
  var parts = [];

  function rnd(key) {
    var h = 2166136261;
    var text = String(finish ? finish.seed : 0) + "|" + key;
    for (var i = 0; i < text.length; i++) h = Math.imul(h ^ text.charCodeAt(i), 16777619) >>> 0;
    var s = h || 1;
    return function () {
      s = (s + 0x6D2B79F5) >>> 0;
      var t = Math.imul(s ^ (s >>> 15), s | 1);
      t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
      return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
  }

  function strength(name) {
    var v = finish ? Number(finish[name]) : 0;
    return isFinite(v) ? Math.max(0, Math.min(1, v)) : 0;
  }

  function rgb(hex) {
    var m = /^#?([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})/i.exec(String(hex || ""));
    return m ? [parseInt(m[1], 16), parseInt(m[2], 16), parseInt(m[3], 16)] : [255, 255, 255];
  }

  // A tile of seeded specks: "tooth" = light pits of the paper (two scales of value noise, tiled), "grain" = light and dark film grain.
  function tile(kind, size, key) {
    var c = document.createElement("canvas");
    c.width = size;
    c.height = size;
    var g = c.getContext("2d");
    if (!g) return null;
    var r = rnd(key);
    var data = g.createImageData(size, size);
    var d = data.data;
    var fields = kind === "tooth" ? [field(r, size, 8), field(r, size, 32)] : null;
    for (var y = 0, i = 0; y < size; y++) {
      for (var x = 0; x < size; x++, i += 4) {
        if (fields) {
          var v = 0.55 * fields[0](x, y) + 0.3 * fields[1](x, y) + 0.15 * r();
          var a = Math.max(0, Math.min(1, (v - 0.52) / 0.25));
          d[i] = 255; d[i + 1] = 251; d[i + 2] = 242; d[i + 3] = Math.round(255 * a * a);
        } else if (r() < 0.45) {
          var light = r() < 0.5;
          d[i] = d[i + 1] = d[i + 2] = light ? 255 : 0;
          d[i + 3] = Math.round(255 * (0.25 + 0.75 * r()));
        }
      }
    }
    g.putImageData(data, 0, 0);
    return c;
  }

  // Smooth value noise on a grid of cells x cells that wraps at the tile's edge (so the tile repeats without a seam).
  function field(r, size, cells) {
    var grid = [];
    for (var i = 0; i < cells * cells; i++) grid.push(r());
    return function (x, y) {
      var fx = x / size * cells, fy = y / size * cells;
      var ix = Math.floor(fx), iy = Math.floor(fy);
      var tx = fx - ix, ty = fy - iy;
      tx = tx * tx * (3 - 2 * tx);
      ty = ty * ty * (3 - 2 * ty);
      var at = function (cx, cy) { return grid[((cy % cells) * cells) + (cx % cells)]; };
      var top = at(ix, iy) + (at(ix + 1, iy) - at(ix, iy)) * tx;
      var bottom = at(ix, iy + 1) + (at(ix + 1, iy + 1) - at(ix, iy + 1)) * tx;
      return top + (bottom - top) * ty;
    };
  }

  function shadowCss(depth) {
    var s = strength("shadow") * depth;
    var k = unit / 1080;
    var alpha = Math.min(0.6, 0.2 + 0.09 * s);
    var off = (1.5 + 4.5 * s) * k, blur = (2 + 6 * s) * k;
    return "drop-shadow(" + (0.5 * off).toFixed(1) + "px " + (0.86 * off).toFixed(1) + "px " + blur.toFixed(1) + "px rgba(48,28,14," + alpha.toFixed(2) + "))";
  }

  function finishLayer() {
    var canvas = el("canvas", root, "position:absolute;left:0;top:0;width:100%;height:100%;pointer-events:none;");
    canvas.setAttribute("data-tm-finish", "1");
    canvas.width = W;
    canvas.height = H;
    var g = canvas.getContext("2d");
    if (!g) return;

    // The still part, made once: tooth, vignette and glow.
    var still = document.createElement("canvas");
    still.width = W;
    still.height = H;
    var s = still.getContext("2d");
    if (s && strength("tooth") > 0) {
      var tooth = tile("tooth", 256, "tooth");
      if (tooth) {
        s.globalAlpha = 0.55 * strength("tooth");
        s.fillStyle = s.createPattern(tooth, "repeat");
        s.fillRect(0, 0, W, H);
        s.globalAlpha = 1;
      }
    }

    if (s && strength("bloom") > 0) {
      var r = rnd("bloom");
      var glowColor = rgb(C.accent);
      s.globalCompositeOperation = "lighter";
      for (var i = 0; i < 3; i++) {
        var gx = W * (0.25 + 0.5 * r()), gy = H * (0.25 + 0.5 * r()), radius = unit * (0.45 + 0.35 * r());
        var glowFill = s.createRadialGradient(gx, gy, 0, gx, gy, radius);
        glowFill.addColorStop(0, "rgba(" + glowColor.join(",") + "," + (0.16 * strength("bloom")).toFixed(3) + ")");
        glowFill.addColorStop(1, "rgba(" + glowColor.join(",") + ",0)");
        s.fillStyle = glowFill;
        s.fillRect(0, 0, W, H);
      }
      s.globalCompositeOperation = "source-over";
    }

    if (s && strength("vignette") > 0) {
      var reach = Math.sqrt(W * W + H * H) / 2;
      var shade = s.createRadialGradient(W / 2, H / 2, reach * 0.5, W / 2, H / 2, reach);
      shade.addColorStop(0, "rgba(12,8,20,0)");
      shade.addColorStop(1, "rgba(12,8,20," + (0.6 * strength("vignette")).toFixed(3) + ")");
      s.fillStyle = shade;
      s.fillRect(0, 0, W, H);
    }

    // The shadow by depth, set once: the drawings one step above the scene, the labels and pins two.
    if (strength("shadow") > 0) {
      parts.forEach(function (p) {
        p.svg.style.filter = shadowCss(1);
        p.overlay.style.filter = shadowCss(2);
      });
    }

    var grains = [];
    if (strength("grain") > 0) {
      for (var n = 0; n < 4; n++) {
        var grain = tile("grain", 200, "grain" + n);
        if (grain) grains.push(g.createPattern(grain, "repeat"));
      }
    }

    var boil = strength("boil") * unit * 0.0025 * (calm ? 0.5 : 1);
    var steps = finish.steps && finish.steps.length >= 24 ? finish.steps : null;
    var painted = -1;

    function paint(time) {
      var step = Math.max(0, Math.floor(time * 12 + 1e-6));
      if (step === painted) return;
      painted = step;
      g.clearRect(0, 0, W, H);
      g.drawImage(still, 0, 0);
      if (grains.length > 0) {
        var r = rnd("grain-step" + step);
        g.save();
        g.globalAlpha = 0.4 * strength("grain");
        g.translate(-Math.floor(r() * 200), -Math.floor(r() * 200));
        g.fillStyle = grains[step % grains.length];
        g.fillRect(0, 0, W + 200, H + 200);
        g.restore();
      }

      if (boil > 0) {
        parts.forEach(function (p, i) {
          var k = ((step + i * 5) % 12) * 2;
          var dx = steps ? steps[k] : rnd("boil" + step + "-" + i)() * 2 - 1;
          var dy = steps ? steps[k + 1] : rnd("boil" + step + "-" + i + "y")() * 2 - 1;
          p.camera.style.translate = (dx * boil).toFixed(2) + "px " + (dy * boil).toFixed(2) + "px";
        });
      }
    }

    var length = input.durationSeconds || tl.duration();
    var clock = { t: 0 };
    paint(0);
    tl.to(clock, { t: length, duration: length, ease: "none", onUpdate: function () { paint(tl.time()); } }, 0);
  }

  var previous = null;
  input.scenes.forEach(function (scene, index) {
    var plan = scene.plan || { base: scene.image ? "image" : "title" };
    var start = scene.startSeconds, end = scene.startSeconds + scene.durationSeconds;
    var layer = el("div", root, "position:absolute;left:0;top:0;width:100%;height:100%;overflow:hidden;opacity:0;background:" + C.background + ";");
    transitionIn(layer, previous, scene, plan, start);
    previous = layer;
    var camera = el("div", layer, "position:absolute;left:0;top:0;width:100%;height:100%;transform-origin:0 0;");
    var project = null;
    var countries = {};
    if (scene.image && plan.base === "image") {
      var picture = el("img", camera, "position:absolute;left:0;top:0;width:100%;height:100%;object-fit:cover;");
      picture.src = scene.image;
      // A slow drift keeps a still picture alive; the plan's camera takes over at its beats. A tight tempo (0.1.42, plan.drift) moves it the
      // film's way (a pan at 1.12 over 80 % of the room that leaves) or pushes in; otherwise the slight zoom it always had.
      if (!(plan.beats || []).some(function (b) { return b.camera; })) {
        var drift = plan.drift || "";
        var big = calm ? 1.06 : 1.12;
        if (shifts[drift]) {
          var room = (big - 1) / 2 * 0.8;
          var s = shifts[drift];
          tl.fromTo(camera, { scale: big, x: s[0] * room * W, y: s[1] * room * H, transformOrigin: "50% 50%" },
            { scale: big, x: -s[0] * room * W, y: -s[1] * room * H, duration: scene.durationSeconds, ease: "sine.inOut", transformOrigin: "50% 50%" }, start);
        } else if (drift === "in") {
          tl.fromTo(camera, { scale: 1, transformOrigin: "50% 50%" }, { scale: big, duration: scene.durationSeconds, ease: "power1.inOut", transformOrigin: "50% 50%" }, start);
        } else {
          tl.fromTo(camera, { scale: 1 }, { scale: calm ? 1.04 : 1.08, duration: scene.durationSeconds, ease: "none", transformOrigin: "50% 50%" }, start);
        }
      }
    }

    var svg = sv("svg", camera, { viewBox: "0 0 " + W + " " + H, width: W, height: H });
    svg.style.position = "absolute";
    svg.style.left = "0px";
    svg.style.setProperty("top", "0px");
    svg.style.overflow = "visible";
    if (plan.base === "map") {
      project = projector(plan.map ? plan.map.region : [-180, -60, 180, 80]);
      countries = drawMap(svg, plan);
    } else if (plan.base === "diagram") {
      drawDiagram(svg, camera, plan);
    } else if (plan.base === "title" || (!scene.image && plan.base === "image")) {
      var heading = TM.title(plan.title || scene.visual || input.title);
      camera.appendChild(heading);
      enter(heading, start + 0.2);
    }

    var overlay = el("div", camera, "position:absolute;left:0;top:0;width:100%;height:100%;");
    if (index === 0 && plan.base !== "title" && input.title) {
      var opening = TM.title(input.title);
      layer.appendChild(opening);
      tl.fromTo(opening, { opacity: 0, y: unit * 0.03 }, { opacity: 1, y: 0, duration: 0.5 }, start + 0.1);
      tl.to(opening, { opacity: 0, duration: 0.4 }, start + Math.min(scene.durationSeconds - 0.5, 3.2));
    }

    var ctx = { plan: plan, svg: svg, overlay: overlay, project: project, countries: countries, camera: camera, layer: layer, sceneStart: start };
    var beats = plan.beats || [];
    beats.forEach(function (beat) {
      var span = Math.max(0.5, beat.duration || 1);
      if (!calm) cameraTo(ctx, beat.camera, beat.start, span);
      (beat.actions || []).forEach(function (a) { act(a, ctx, beat.start, span); });
    });
    if (calm && beats.length > 0 && beats[0].camera) cameraTo(ctx, beats[0].camera, start, scene.durationSeconds);
    presenter(ctx, beats, start, end);
    parts.push({ svg: svg, overlay: overlay, camera: camera });
  });

  if (finish) finishLayer();

  window.__timelines = window.__timelines || {};
  window.__timelines["concept"] = tl;
})();
