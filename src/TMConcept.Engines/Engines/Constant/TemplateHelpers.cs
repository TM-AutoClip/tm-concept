namespace TMConcept.Engines.Engines;

/// <summary>
/// The app's template helper files written into an engine project next to the model's code (the model cannot change them; the video code
/// prompt names what they offer, <see cref="Core.Videos.VideoStylePrompt.Helpers"/>). They read everything from the input's <c>style</c>
/// (falling back to <c>theme</c> for a video without a template), so the same file serves every video. Ready helpers shorten the code a model
/// writes (fewer output tokens, which matters most for local models) and keep titles, colours and fonts the template's.
/// </summary>
public static class TemplateHelpers
{
    /// <summary>HyperFrames: <c>tm.js</c>, loaded after <c>window.INPUT</c> and before <c>scene.js</c>; defines <c>window.TM</c>.</summary>
    public const string HyperFramesScript = """
        // The app's file (TM Concept): template helpers for scene.js; not changed by the model.
        (function () {
          var input = window.INPUT || {};
          var style = input.style || {};
          var colors = style.colors || {};
          var theme = input.theme || {};
          var root = document.getElementById("root");
          var SVG = "http://www.w3.org/2000/svg";
          var width = input.width || 1080;
          var height = input.height || 1920;
          var unit = Math.min(width, height);
          var margin = (style.safeMargin || 0.08) * unit;
          var bottom = (style.safeBottom || 0.08) * height;
          var paces = { calm: 1.5, normal: 1, lively: 0.7 };

          function color(name) {
            if (colors[name]) return colors[name];
            if (name === "background") return theme.background || "#000000";
            if (name === "accent" || name === "accent2") return theme.accent || "#FFFFFF";
            return theme.text || "#FFFFFF";
          }

          var font = style.font ? '"' + style.font + '", sans-serif' : "sans-serif";

          function title(text) {
            var x = typeof style.titleX === "number" ? style.titleX : 0.5;
            var y = typeof style.titleY === "number" ? style.titleY : 0;
            var kind = style.titleStyle || "plain";
            var align = x < 0.25 ? "left" : x > 0.75 ? "right" : "center";
            var el = document.createElement("div");
            el.className = "tm-title tm-title-" + kind + " tm-align-" + align;
            el.textContent = text;
            el.style.position = "absolute";
            el.style.top = Math.round(margin + y * (height - margin - bottom)) + "px";
            el.style.textAlign = align;
            if (kind === "band") {
              el.style.left = "0px";
              el.style.right = "0px";
              el.style.paddingLeft = el.style.paddingRight = Math.round(margin) + "px";
              el.style.transform = "translate(0%, " + (-y * 100) + "%)";
            } else {
              el.style.left = Math.round(margin + x * (width - 2 * margin)) + "px";
              el.style.maxWidth = Math.round(width - 2 * margin) + "px";
              el.style.transform = "translate(" + (-x * 100) + "%, " + (-y * 100) + "%)";
            }
            return el;
          }

          var poses = {
            stand: [[50, 75, 25, 115], [50, 75, 75, 115], [50, 125, 30, 190], [50, 125, 70, 190]],
            walk: [[50, 75, 30, 108], [50, 75, 72, 105], [50, 125, 25, 185], [50, 125, 78, 180]],
            point: [[50, 75, 22, 112], [50, 75, 98, 62], [50, 125, 30, 190], [50, 125, 70, 190]],
            cheer: [[50, 75, 18, 35], [50, 75, 82, 35], [50, 125, 30, 190], [50, 125, 70, 190]],
            think: [[50, 75, 25, 115], [50, 75, 74, 96], [74, 96, 58, 50], [50, 125, 30, 190], [50, 125, 70, 190]]
          };

          function figure(pose) {
            var stroke = style.drawStyle === "neon" ? color("accent") : color("text");
            var svg = document.createElementNS(SVG, "svg");
            svg.setAttribute("viewBox", "0 0 100 200");
            svg.setAttribute("fill", "none");
            svg.setAttribute("stroke", stroke);
            svg.setAttribute("stroke-width", "7");
            svg.setAttribute("stroke-linecap", "round");
            svg.setAttribute("stroke-linejoin", "round");
            svg.style.position = "absolute";
            svg.style.height = Math.round(height * 0.3) + "px";
            if (style.drawStyle === "neon") svg.style.filter = "drop-shadow(0 0 8px " + stroke + ")";
            var head = document.createElementNS(SVG, "circle");
            head.setAttribute("cx", "50");
            head.setAttribute("cy", "30");
            head.setAttribute("r", "20");
            svg.appendChild(head);
            var lines = [[50, 50, 50, 125]].concat(poses[pose] || poses.stand);
            lines.forEach(function (l) {
              var line = document.createElementNS(SVG, "line");
              line.setAttribute("x1", String(l[0]));
              line.setAttribute("y1", String(l[1]));
              line.setAttribute("x2", String(l[2]));
              line.setAttribute("y2", String(l[3]));
              svg.appendChild(line);
            });
            return svg;
          }

          if (document.fonts && style.font) {
            document.fonts.load("400 40px " + font);
            document.fonts.load("700 40px " + font);
          }

          window.TM = Object.freeze({
            color: color,
            font: font,
            stage: function () { return root; },
            title: title,
            figure: figure,
            margin: margin,
            bottom: bottom,
            pace: paces[style.motion] || 1
          });
        })();
        """;

    /// <summary>Remotion: <c>src/tm.tsx</c> (<c>import {tmColor, tmFont, TmTitle} from './tm';</c>).</summary>
    public const string RemotionHelpers = """
        // The app's file (TM Concept): template helpers for Scene.tsx; not changed by the model.
        import React from 'react';
        import type {ConceptInput} from './input';

        export type TmColorName = 'background' | 'surface' | 'accent' | 'accent2' | 'text' | 'muted';

        export const tmColor = (input: ConceptInput, name: TmColorName): string => {
          const colors = input.style?.colors;
          if (colors) {
            return colors[name];
          }

          return name === 'background' ? input.theme.background : name === 'accent' || name === 'accent2' ? input.theme.accent : input.theme.text;
        };

        export const tmFont = (input: ConceptInput): string => (input.style ? `"${input.style.font}", sans-serif` : 'sans-serif');

        export const TmTitle: React.FC<{input: ConceptInput; text: string; style?: React.CSSProperties}> = ({input, text, style}) => {
          const s = input.style;
          const unit = Math.min(input.width, input.height);
          const margin = (s?.safeMargin ?? 0.08) * unit;
          const bottom = (s?.safeBottom ?? 0.08) * input.height;
          const x = s?.titleX ?? 0.5;
          const y = s?.titleY ?? 0;
          const kind = s?.titleStyle ?? 'plain';
          const align: React.CSSProperties['textAlign'] = x < 0.25 ? 'left' : x > 0.75 ? 'right' : 'center';
          const place: React.CSSProperties = kind === 'band'
            ? {left: 0, right: 0, padding: `${unit * 0.025}px ${margin}px`, background: tmColor(input, 'surface'), transform: `translateY(${-y * 100}%)`}
            : {left: margin + x * (input.width - 2 * margin), maxWidth: input.width - 2 * margin, transform: `translate(${-x * 100}%, ${-y * 100}%)`};
          const look: React.CSSProperties = kind === 'boxed'
            ? {background: tmColor(input, 'accent'), color: tmColor(input, 'background'), padding: `${unit * 0.02}px ${unit * 0.04}px`, borderRadius: unit * 0.025}
            : kind === 'outline'
              ? {WebkitTextStroke: `${unit * 0.005}px ${tmColor(input, 'background')}`, paintOrder: 'stroke fill'}
              : {};
          return (
            <div style={{position: 'absolute', top: margin + y * (input.height - margin - bottom), fontFamily: tmFont(input), fontWeight: 700,
              fontSize: unit * 0.075, lineHeight: 1.15, color: tmColor(input, 'text'), textAlign: align, ...place, ...look, ...style}}>
              {text}
              {kind === 'underline' ? (
                <div style={{width: unit * 0.16, height: unit * 0.009, marginTop: unit * 0.015, background: tmColor(input, 'accent'),
                  marginLeft: align === 'left' ? 0 : 'auto', marginRight: align === 'right' ? 0 : 'auto'}} />
              ) : null}
            </div>
          );
        };
        """;

    /// <summary>Revideo: <c>src/tm.ts</c> (<c>import {tmColor, tmFont} from './tm';</c>).</summary>
    public const string RevideoHelpers = """
        // The app's file (TM Concept): template helpers for scene.tsx; not changed by the model.
        import type {ConceptInput} from './input';

        export type TmColorName = 'background' | 'surface' | 'accent' | 'accent2' | 'text' | 'muted';

        export const tmColor = (input: ConceptInput, name: TmColorName): string => {
          const colors = input.style?.colors;
          if (colors) {
            return colors[name];
          }

          return name === 'background' ? input.theme.background : name === 'accent' || name === 'accent2' ? input.theme.accent : input.theme.text;
        };

        export const tmFont = (input: ConceptInput): string => (input.style ? input.style.font : 'sans-serif');

        // Readable text: give every Txt these props (an outline in the colour opposite to the text); empty when the template has it off.
        export const tmOutline = (input: ConceptInput): {stroke?: string; lineWidth?: number; strokeFirst?: boolean} =>
          input.style?.outline ? {stroke: input.style.outline, lineWidth: Math.min(input.width, input.height) * 0.006, strokeFirst: true} : {};
        """;
}
