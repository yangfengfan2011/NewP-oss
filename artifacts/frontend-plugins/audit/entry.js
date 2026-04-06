import we, { useState as T, useEffect as Se } from "react";
var Q = { exports: {} }, M = {};
/**
 * @license React
 * react-jsx-runtime.production.min.js
 *
 * Copyright (c) Facebook, Inc. and its affiliates.
 *
 * This source code is licensed under the MIT license found in the
 * LICENSE file in the root directory of this source tree.
 */
var Ne;
function fr() {
  if (Ne) return M;
  Ne = 1;
  var v = we, w = Symbol.for("react.element"), O = Symbol.for("react.fragment"), _ = Object.prototype.hasOwnProperty, j = v.__SECRET_INTERNALS_DO_NOT_USE_OR_YOU_WILL_BE_FIRED.ReactCurrentOwner, S = { key: !0, ref: !0, __self: !0, __source: !0 };
  function p(y, d, N) {
    var m, E = {}, c = null, o = null;
    N !== void 0 && (c = "" + N), d.key !== void 0 && (c = "" + d.key), d.ref !== void 0 && (o = d.ref);
    for (m in d) _.call(d, m) && !S.hasOwnProperty(m) && (E[m] = d[m]);
    if (y && y.defaultProps) for (m in d = y.defaultProps, d) E[m] === void 0 && (E[m] = d[m]);
    return { $$typeof: w, type: y, key: c, ref: o, props: E, _owner: j.current };
  }
  return M.Fragment = O, M.jsx = p, M.jsxs = p, M;
}
var V = {};
/**
 * @license React
 * react-jsx-runtime.development.js
 *
 * Copyright (c) Facebook, Inc. and its affiliates.
 *
 * This source code is licensed under the MIT license found in the
 * LICENSE file in the root directory of this source tree.
 */
var Te;
function hr() {
  return Te || (Te = 1, process.env.NODE_ENV !== "production" && function() {
    var v = we, w = Symbol.for("react.element"), O = Symbol.for("react.portal"), _ = Symbol.for("react.fragment"), j = Symbol.for("react.strict_mode"), S = Symbol.for("react.profiler"), p = Symbol.for("react.provider"), y = Symbol.for("react.context"), d = Symbol.for("react.forward_ref"), N = Symbol.for("react.suspense"), m = Symbol.for("react.suspense_list"), E = Symbol.for("react.memo"), c = Symbol.for("react.lazy"), o = Symbol.for("react.offscreen"), P = Symbol.iterator, Ce = "@@iterator";
    function Oe(e) {
      if (e === null || typeof e != "object")
        return null;
      var a = P && e[P] || e[Ce];
      return typeof a == "function" ? a : null;
    }
    var I = v.__SECRET_INTERNALS_DO_NOT_USE_OR_YOU_WILL_BE_FIRED;
    function g(e) {
      {
        for (var a = arguments.length, s = new Array(a > 1 ? a - 1 : 0), t = 1; t < a; t++)
          s[t - 1] = arguments[t];
        Pe("error", e, s);
      }
    }
    function Pe(e, a, s) {
      {
        var t = I.ReactDebugCurrentFrame, l = t.getStackAddendum();
        l !== "" && (a += "%s", s = s.concat([l]));
        var u = s.map(function(i) {
          return String(i);
        });
        u.unshift("Warning: " + a), Function.prototype.apply.call(console[e], console, u);
      }
    }
    var ke = !1, Ae = !1, Ie = !1, De = !1, Fe = !1, ee;
    ee = Symbol.for("react.module.reference");
    function Le(e) {
      return !!(typeof e == "string" || typeof e == "function" || e === _ || e === S || Fe || e === j || e === N || e === m || De || e === o || ke || Ae || Ie || typeof e == "object" && e !== null && (e.$$typeof === c || e.$$typeof === E || e.$$typeof === p || e.$$typeof === y || e.$$typeof === d || // This needs to include all possible module reference object
      // types supported by any Flight configuration anywhere since
      // we don't know which Flight build this will end up being used
      // with.
      e.$$typeof === ee || e.getModuleId !== void 0));
    }
    function $e(e, a, s) {
      var t = e.displayName;
      if (t)
        return t;
      var l = a.displayName || a.name || "";
      return l !== "" ? s + "(" + l + ")" : s;
    }
    function re(e) {
      return e.displayName || "Context";
    }
    function C(e) {
      if (e == null)
        return null;
      if (typeof e.tag == "number" && g("Received an unexpected object in getComponentNameFromType(). This is likely a bug in React. Please file an issue."), typeof e == "function")
        return e.displayName || e.name || null;
      if (typeof e == "string")
        return e;
      switch (e) {
        case _:
          return "Fragment";
        case O:
          return "Portal";
        case S:
          return "Profiler";
        case j:
          return "StrictMode";
        case N:
          return "Suspense";
        case m:
          return "SuspenseList";
      }
      if (typeof e == "object")
        switch (e.$$typeof) {
          case y:
            var a = e;
            return re(a) + ".Consumer";
          case p:
            var s = e;
            return re(s._context) + ".Provider";
          case d:
            return $e(e, e.render, "ForwardRef");
          case E:
            var t = e.displayName || null;
            return t !== null ? t : C(e.type) || "Memo";
          case c: {
            var l = e, u = l._payload, i = l._init;
            try {
              return C(i(u));
            } catch {
              return null;
            }
          }
        }
      return null;
    }
    var k = Object.assign, L = 0, ae, se, te, ne, ie, le, ce;
    function oe() {
    }
    oe.__reactDisabledLog = !0;
    function Me() {
      {
        if (L === 0) {
          ae = console.log, se = console.info, te = console.warn, ne = console.error, ie = console.group, le = console.groupCollapsed, ce = console.groupEnd;
          var e = {
            configurable: !0,
            enumerable: !0,
            value: oe,
            writable: !0
          };
          Object.defineProperties(console, {
            info: e,
            log: e,
            warn: e,
            error: e,
            group: e,
            groupCollapsed: e,
            groupEnd: e
          });
        }
        L++;
      }
    }
    function Ve() {
      {
        if (L--, L === 0) {
          var e = {
            configurable: !0,
            enumerable: !0,
            writable: !0
          };
          Object.defineProperties(console, {
            log: k({}, e, {
              value: ae
            }),
            info: k({}, e, {
              value: se
            }),
            warn: k({}, e, {
              value: te
            }),
            error: k({}, e, {
              value: ne
            }),
            group: k({}, e, {
              value: ie
            }),
            groupCollapsed: k({}, e, {
              value: le
            }),
            groupEnd: k({}, e, {
              value: ce
            })
          });
        }
        L < 0 && g("disabledDepth fell below zero. This is a bug in React. Please file an issue.");
      }
    }
    var z = I.ReactCurrentDispatcher, B;
    function W(e, a, s) {
      {
        if (B === void 0)
          try {
            throw Error();
          } catch (l) {
            var t = l.stack.trim().match(/\n( *(at )?)/);
            B = t && t[1] || "";
          }
        return `
` + B + e;
      }
    }
    var J = !1, Y;
    {
      var We = typeof WeakMap == "function" ? WeakMap : Map;
      Y = new We();
    }
    function ue(e, a) {
      if (!e || J)
        return "";
      {
        var s = Y.get(e);
        if (s !== void 0)
          return s;
      }
      var t;
      J = !0;
      var l = Error.prepareStackTrace;
      Error.prepareStackTrace = void 0;
      var u;
      u = z.current, z.current = null, Me();
      try {
        if (a) {
          var i = function() {
            throw Error();
          };
          if (Object.defineProperty(i.prototype, "props", {
            set: function() {
              throw Error();
            }
          }), typeof Reflect == "object" && Reflect.construct) {
            try {
              Reflect.construct(i, []);
            } catch (b) {
              t = b;
            }
            Reflect.construct(e, [], i);
          } else {
            try {
              i.call();
            } catch (b) {
              t = b;
            }
            e.call(i.prototype);
          }
        } else {
          try {
            throw Error();
          } catch (b) {
            t = b;
          }
          e();
        }
      } catch (b) {
        if (b && t && typeof b.stack == "string") {
          for (var n = b.stack.split(`
`), x = t.stack.split(`
`), f = n.length - 1, h = x.length - 1; f >= 1 && h >= 0 && n[f] !== x[h]; )
            h--;
          for (; f >= 1 && h >= 0; f--, h--)
            if (n[f] !== x[h]) {
              if (f !== 1 || h !== 1)
                do
                  if (f--, h--, h < 0 || n[f] !== x[h]) {
                    var R = `
` + n[f].replace(" at new ", " at ");
                    return e.displayName && R.includes("<anonymous>") && (R = R.replace("<anonymous>", e.displayName)), typeof e == "function" && Y.set(e, R), R;
                  }
                while (f >= 1 && h >= 0);
              break;
            }
        }
      } finally {
        J = !1, z.current = u, Ve(), Error.prepareStackTrace = l;
      }
      var F = e ? e.displayName || e.name : "", A = F ? W(F) : "";
      return typeof e == "function" && Y.set(e, A), A;
    }
    function Ye(e, a, s) {
      return ue(e, !1);
    }
    function Ue(e) {
      var a = e.prototype;
      return !!(a && a.isReactComponent);
    }
    function U(e, a, s) {
      if (e == null)
        return "";
      if (typeof e == "function")
        return ue(e, Ue(e));
      if (typeof e == "string")
        return W(e);
      switch (e) {
        case N:
          return W("Suspense");
        case m:
          return W("SuspenseList");
      }
      if (typeof e == "object")
        switch (e.$$typeof) {
          case d:
            return Ye(e.render);
          case E:
            return U(e.type, a, s);
          case c: {
            var t = e, l = t._payload, u = t._init;
            try {
              return U(u(l), a, s);
            } catch {
            }
          }
        }
      return "";
    }
    var $ = Object.prototype.hasOwnProperty, de = {}, fe = I.ReactDebugCurrentFrame;
    function q(e) {
      if (e) {
        var a = e._owner, s = U(e.type, e._source, a ? a.type : null);
        fe.setExtraStackFrame(s);
      } else
        fe.setExtraStackFrame(null);
    }
    function qe(e, a, s, t, l) {
      {
        var u = Function.call.bind($);
        for (var i in e)
          if (u(e, i)) {
            var n = void 0;
            try {
              if (typeof e[i] != "function") {
                var x = Error((t || "React class") + ": " + s + " type `" + i + "` is invalid; it must be a function, usually from the `prop-types` package, but received `" + typeof e[i] + "`.This often happens because of typos such as `PropTypes.function` instead of `PropTypes.func`.");
                throw x.name = "Invariant Violation", x;
              }
              n = e[i](a, i, t, s, null, "SECRET_DO_NOT_PASS_THIS_OR_YOU_WILL_BE_FIRED");
            } catch (f) {
              n = f;
            }
            n && !(n instanceof Error) && (q(l), g("%s: type specification of %s `%s` is invalid; the type checker function must return `null` or an `Error` but returned a %s. You may have forgotten to pass an argument to the type checker creator (arrayOf, instanceOf, objectOf, oneOf, oneOfType, and shape all require an argument).", t || "React class", s, i, typeof n), q(null)), n instanceof Error && !(n.message in de) && (de[n.message] = !0, q(l), g("Failed %s type: %s", s, n.message), q(null));
          }
      }
    }
    var ze = Array.isArray;
    function K(e) {
      return ze(e);
    }
    function Be(e) {
      {
        var a = typeof Symbol == "function" && Symbol.toStringTag, s = a && e[Symbol.toStringTag] || e.constructor.name || "Object";
        return s;
      }
    }
    function Je(e) {
      try {
        return he(e), !1;
      } catch {
        return !0;
      }
    }
    function he(e) {
      return "" + e;
    }
    function pe(e) {
      if (Je(e))
        return g("The provided key is an unsupported type %s. This value must be coerced to a string before before using it here.", Be(e)), he(e);
    }
    var me = I.ReactCurrentOwner, Ke = {
      key: !0,
      ref: !0,
      __self: !0,
      __source: !0
    }, ve, ge;
    function Ge(e) {
      if ($.call(e, "ref")) {
        var a = Object.getOwnPropertyDescriptor(e, "ref").get;
        if (a && a.isReactWarning)
          return !1;
      }
      return e.ref !== void 0;
    }
    function Xe(e) {
      if ($.call(e, "key")) {
        var a = Object.getOwnPropertyDescriptor(e, "key").get;
        if (a && a.isReactWarning)
          return !1;
      }
      return e.key !== void 0;
    }
    function He(e, a) {
      typeof e.ref == "string" && me.current;
    }
    function Ze(e, a) {
      {
        var s = function() {
          ve || (ve = !0, g("%s: `key` is not a prop. Trying to access it will result in `undefined` being returned. If you need to access the same value within the child component, you should pass it as a different prop. (https://reactjs.org/link/special-props)", a));
        };
        s.isReactWarning = !0, Object.defineProperty(e, "key", {
          get: s,
          configurable: !0
        });
      }
    }
    function Qe(e, a) {
      {
        var s = function() {
          ge || (ge = !0, g("%s: `ref` is not a prop. Trying to access it will result in `undefined` being returned. If you need to access the same value within the child component, you should pass it as a different prop. (https://reactjs.org/link/special-props)", a));
        };
        s.isReactWarning = !0, Object.defineProperty(e, "ref", {
          get: s,
          configurable: !0
        });
      }
    }
    var er = function(e, a, s, t, l, u, i) {
      var n = {
        // This tag allows us to uniquely identify this as a React Element
        $$typeof: w,
        // Built-in properties that belong on the element
        type: e,
        key: a,
        ref: s,
        props: i,
        // Record the component responsible for creating this element.
        _owner: u
      };
      return n._store = {}, Object.defineProperty(n._store, "validated", {
        configurable: !1,
        enumerable: !1,
        writable: !0,
        value: !1
      }), Object.defineProperty(n, "_self", {
        configurable: !1,
        enumerable: !1,
        writable: !1,
        value: t
      }), Object.defineProperty(n, "_source", {
        configurable: !1,
        enumerable: !1,
        writable: !1,
        value: l
      }), Object.freeze && (Object.freeze(n.props), Object.freeze(n)), n;
    };
    function rr(e, a, s, t, l) {
      {
        var u, i = {}, n = null, x = null;
        s !== void 0 && (pe(s), n = "" + s), Xe(a) && (pe(a.key), n = "" + a.key), Ge(a) && (x = a.ref, He(a, l));
        for (u in a)
          $.call(a, u) && !Ke.hasOwnProperty(u) && (i[u] = a[u]);
        if (e && e.defaultProps) {
          var f = e.defaultProps;
          for (u in f)
            i[u] === void 0 && (i[u] = f[u]);
        }
        if (n || x) {
          var h = typeof e == "function" ? e.displayName || e.name || "Unknown" : e;
          n && Ze(i, h), x && Qe(i, h);
        }
        return er(e, n, x, l, t, me.current, i);
      }
    }
    var G = I.ReactCurrentOwner, xe = I.ReactDebugCurrentFrame;
    function D(e) {
      if (e) {
        var a = e._owner, s = U(e.type, e._source, a ? a.type : null);
        xe.setExtraStackFrame(s);
      } else
        xe.setExtraStackFrame(null);
    }
    var X;
    X = !1;
    function H(e) {
      return typeof e == "object" && e !== null && e.$$typeof === w;
    }
    function je() {
      {
        if (G.current) {
          var e = C(G.current.type);
          if (e)
            return `

Check the render method of \`` + e + "`.";
        }
        return "";
      }
    }
    function ar(e) {
      return "";
    }
    var be = {};
    function sr(e) {
      {
        var a = je();
        if (!a) {
          var s = typeof e == "string" ? e : e.displayName || e.name;
          s && (a = `

Check the top-level render call using <` + s + ">.");
        }
        return a;
      }
    }
    function ye(e, a) {
      {
        if (!e._store || e._store.validated || e.key != null)
          return;
        e._store.validated = !0;
        var s = sr(a);
        if (be[s])
          return;
        be[s] = !0;
        var t = "";
        e && e._owner && e._owner !== G.current && (t = " It was passed a child from " + C(e._owner.type) + "."), D(e), g('Each child in a list should have a unique "key" prop.%s%s See https://reactjs.org/link/warning-keys for more information.', s, t), D(null);
      }
    }
    function Ee(e, a) {
      {
        if (typeof e != "object")
          return;
        if (K(e))
          for (var s = 0; s < e.length; s++) {
            var t = e[s];
            H(t) && ye(t, a);
          }
        else if (H(e))
          e._store && (e._store.validated = !0);
        else if (e) {
          var l = Oe(e);
          if (typeof l == "function" && l !== e.entries)
            for (var u = l.call(e), i; !(i = u.next()).done; )
              H(i.value) && ye(i.value, a);
        }
      }
    }
    function tr(e) {
      {
        var a = e.type;
        if (a == null || typeof a == "string")
          return;
        var s;
        if (typeof a == "function")
          s = a.propTypes;
        else if (typeof a == "object" && (a.$$typeof === d || // Note: Memo only checks outer props here.
        // Inner props are checked in the reconciler.
        a.$$typeof === E))
          s = a.propTypes;
        else
          return;
        if (s) {
          var t = C(a);
          qe(s, e.props, "prop", t, e);
        } else if (a.PropTypes !== void 0 && !X) {
          X = !0;
          var l = C(a);
          g("Component %s declared `PropTypes` instead of `propTypes`. Did you misspell the property assignment?", l || "Unknown");
        }
        typeof a.getDefaultProps == "function" && !a.getDefaultProps.isReactClassApproved && g("getDefaultProps is only used on classic React.createClass definitions. Use a static property named `defaultProps` instead.");
      }
    }
    function nr(e) {
      {
        for (var a = Object.keys(e.props), s = 0; s < a.length; s++) {
          var t = a[s];
          if (t !== "children" && t !== "key") {
            D(e), g("Invalid prop `%s` supplied to `React.Fragment`. React.Fragment can only have `key` and `children` props.", t), D(null);
            break;
          }
        }
        e.ref !== null && (D(e), g("Invalid attribute `ref` supplied to `React.Fragment`."), D(null));
      }
    }
    var _e = {};
    function Re(e, a, s, t, l, u) {
      {
        var i = Le(e);
        if (!i) {
          var n = "";
          (e === void 0 || typeof e == "object" && e !== null && Object.keys(e).length === 0) && (n += " You likely forgot to export your component from the file it's defined in, or you might have mixed up default and named imports.");
          var x = ar();
          x ? n += x : n += je();
          var f;
          e === null ? f = "null" : K(e) ? f = "array" : e !== void 0 && e.$$typeof === w ? (f = "<" + (C(e.type) || "Unknown") + " />", n = " Did you accidentally export a JSX literal instead of a component?") : f = typeof e, g("React.jsx: type is invalid -- expected a string (for built-in components) or a class/function (for composite components) but got: %s.%s", f, n);
        }
        var h = rr(e, a, s, l, u);
        if (h == null)
          return h;
        if (i) {
          var R = a.children;
          if (R !== void 0)
            if (t)
              if (K(R)) {
                for (var F = 0; F < R.length; F++)
                  Ee(R[F], e);
                Object.freeze && Object.freeze(R);
              } else
                g("React.jsx: Static children should always be an array. You are likely explicitly calling React.jsxs or React.jsxDEV. Use the Babel transform instead.");
            else
              Ee(R, e);
        }
        if ($.call(a, "key")) {
          var A = C(e), b = Object.keys(a).filter(function(dr) {
            return dr !== "key";
          }), Z = b.length > 0 ? "{key: someKey, " + b.join(": ..., ") + ": ...}" : "{key: someKey}";
          if (!_e[A + Z]) {
            var ur = b.length > 0 ? "{" + b.join(": ..., ") + ": ...}" : "{}";
            g(`A props object containing a "key" prop is being spread into JSX:
  let props = %s;
  <%s {...props} />
React keys must be passed directly to JSX without using spread:
  let props = %s;
  <%s key={someKey} {...props} />`, Z, A, ur, A), _e[A + Z] = !0;
          }
        }
        return e === _ ? nr(h) : tr(h), h;
      }
    }
    function ir(e, a, s) {
      return Re(e, a, s, !0);
    }
    function lr(e, a, s) {
      return Re(e, a, s, !1);
    }
    var cr = lr, or = ir;
    V.Fragment = _, V.jsx = cr, V.jsxs = or;
  }()), V;
}
process.env.NODE_ENV === "production" ? Q.exports = fr() : Q.exports = hr();
var r = Q.exports;
function pr() {
  const [v, w] = T([]), [O, _] = T(!0), [j, S] = T(0), [p, y] = T(1), [d] = T(20);
  Se(() => {
    N();
  }, [p]);
  const N = async () => {
    _(!0);
    try {
      const o = await (await fetch(
        `/api/audit/operation-logs?pageIndex=${p}&pageSize=${d}`
      )).json();
      w(o.items), S(o.total);
    } catch (c) {
      console.error("Failed to fetch operation logs:", c);
    } finally {
      _(!1);
    }
  };
  if (O)
    return /* @__PURE__ */ r.jsx("div", { className: "sh-page-loading", children: "操作日志加载中..." });
  const m = v.filter((c) => c.result === "Success").length, E = v.length - m;
  return /* @__PURE__ */ r.jsxs("div", { className: "sh-page", children: [
    /* @__PURE__ */ r.jsx("section", { className: "sh-page-header", children: /* @__PURE__ */ r.jsxs("div", { className: "sh-page-header-main", children: [
      /* @__PURE__ */ r.jsx("span", { className: "sh-page-eyebrow", children: "审计日志" }),
      /* @__PURE__ */ r.jsx("h1", { className: "sh-page-title", children: "操作日志" }),
      /* @__PURE__ */ r.jsx("p", { className: "sh-page-description", children: "查看各模块业务操作审计记录，继续复用统一的摘要区、状态徽标和面板化表格结构。" })
    ] }) }),
    /* @__PURE__ */ r.jsxs("div", { className: "sh-section-grid-3", children: [
      /* @__PURE__ */ r.jsxs("div", { className: "sh-summary-card", children: [
        /* @__PURE__ */ r.jsx("span", { className: "sh-summary-card-label", children: "总记录数" }),
        /* @__PURE__ */ r.jsx("span", { className: "sh-summary-card-value", children: j }),
        /* @__PURE__ */ r.jsxs("span", { className: "sh-summary-card-meta", children: [
          "当前页 ",
          v.length,
          " 条"
        ] })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { className: "sh-summary-card", children: [
        /* @__PURE__ */ r.jsx("span", { className: "sh-summary-card-label", children: "成功操作" }),
        /* @__PURE__ */ r.jsx("span", { className: "sh-summary-card-value", children: m }),
        /* @__PURE__ */ r.jsx("span", { className: "sh-summary-card-meta", children: "当前页统计" })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { className: "sh-summary-card", children: [
        /* @__PURE__ */ r.jsx("span", { className: "sh-summary-card-label", children: "失败操作" }),
        /* @__PURE__ */ r.jsx("span", { className: "sh-summary-card-value", children: E }),
        /* @__PURE__ */ r.jsx("span", { className: "sh-summary-card-meta", children: "当前页统计" })
      ] })
    ] }),
    /* @__PURE__ */ r.jsxs("div", { className: "sh-helper-block", children: [
      /* @__PURE__ */ r.jsx("h3", { className: "sh-helper-block-title", children: "查看说明" }),
      /* @__PURE__ */ r.jsx("p", { children: "当前阶段优先保证跨模块页面结构一致，操作详情抽屉、组合筛选和导出等能力留到后续再补齐。" })
    ] }),
    /* @__PURE__ */ r.jsxs("section", { className: "sh-page-panel", children: [
      /* @__PURE__ */ r.jsxs("div", { className: "sh-page-panel-header", children: [
        /* @__PURE__ */ r.jsx("h2", { className: "sh-page-panel-title", children: "操作记录" }),
        /* @__PURE__ */ r.jsxs("div", { className: "sh-page-panel-meta", children: [
          "第 ",
          p,
          " 页"
        ] })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { className: "sh-page-panel-body", children: [
        /* @__PURE__ */ r.jsx("div", { className: "sh-table-wrap", children: /* @__PURE__ */ r.jsxs("table", { className: "sh-table", children: [
          /* @__PURE__ */ r.jsx("thead", { children: /* @__PURE__ */ r.jsxs("tr", { children: [
            /* @__PURE__ */ r.jsx("th", { children: "时间" }),
            /* @__PURE__ */ r.jsx("th", { children: "用户" }),
            /* @__PURE__ */ r.jsx("th", { children: "模块" }),
            /* @__PURE__ */ r.jsx("th", { children: "操作类型" }),
            /* @__PURE__ */ r.jsx("th", { children: "目标" }),
            /* @__PURE__ */ r.jsx("th", { children: "结果" }),
            /* @__PURE__ */ r.jsx("th", { children: "IP" })
          ] }) }),
          /* @__PURE__ */ r.jsx("tbody", { children: v.map((c) => /* @__PURE__ */ r.jsxs("tr", { children: [
            /* @__PURE__ */ r.jsx("td", { children: new Date(c.operationTime).toLocaleString() }),
            /* @__PURE__ */ r.jsx("td", { children: c.userName || "-" }),
            /* @__PURE__ */ r.jsx("td", { children: c.moduleId }),
            /* @__PURE__ */ r.jsx("td", { children: c.operationType }),
            /* @__PURE__ */ r.jsx("td", { children: c.operationTarget || "-" }),
            /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx(
              "span",
              {
                className: `sh-status-badge ${c.result === "Success" ? "sh-status-badge-success" : "sh-status-badge-danger"}`,
                children: c.result === "Success" ? "成功" : "失败"
              }
            ) }),
            /* @__PURE__ */ r.jsx("td", { children: c.clientIp || "-" })
          ] }, c.id)) })
        ] }) }),
        /* @__PURE__ */ r.jsxs("div", { className: "sh-pagination", children: [
          /* @__PURE__ */ r.jsxs("div", { className: "sh-pagination-info", children: [
            "共 ",
            j,
            " 条记录"
          ] }),
          /* @__PURE__ */ r.jsxs("div", { className: "sh-pagination-actions", children: [
            /* @__PURE__ */ r.jsx(
              "button",
              {
                className: "sh-btn",
                onClick: () => y((c) => Math.max(1, c - 1)),
                disabled: p === 1,
                children: "上一页"
              }
            ),
            /* @__PURE__ */ r.jsxs("span", { className: "sh-pagination-info", children: [
              "第 ",
              p,
              " / ",
              Math.ceil(j / d) || 1,
              " 页"
            ] }),
            /* @__PURE__ */ r.jsx(
              "button",
              {
                className: "sh-btn",
                onClick: () => y((c) => c + 1),
                disabled: p >= Math.ceil(j / d),
                children: "下一页"
              }
            )
          ] })
        ] })
      ] })
    ] })
  ] });
}
function mr() {
  const [v, w] = T([]), [O, _] = T(!0), [j, S] = T(0), [p, y] = T(1), [d] = T(20);
  Se(() => {
    N();
  }, [p]);
  const N = async () => {
    _(!0);
    try {
      const P = await (await fetch(
        `/api/audit/access-logs?pageIndex=${p}&pageSize=${d}`
      )).json();
      w(P.items), S(P.total);
    } catch (o) {
      console.error("Failed to fetch access logs:", o);
    } finally {
      _(!1);
    }
  };
  if (O)
    return /* @__PURE__ */ r.jsx("div", { className: "sh-page-loading", children: "访问日志加载中..." });
  const m = v.filter((o) => o.statusCode < 400).length, E = v.length - m, c = v.length > 0 ? Math.round(v.reduce((o, P) => o + P.responseTime, 0) / v.length) : 0;
  return /* @__PURE__ */ r.jsxs("div", { className: "sh-page", children: [
    /* @__PURE__ */ r.jsx("section", { className: "sh-page-header", children: /* @__PURE__ */ r.jsxs("div", { className: "sh-page-header-main", children: [
      /* @__PURE__ */ r.jsx("span", { className: "sh-page-eyebrow", children: "审计日志" }),
      /* @__PURE__ */ r.jsx("h1", { className: "sh-page-title", children: "访问日志" }),
      /* @__PURE__ */ r.jsx("p", { className: "sh-page-description", children: "查看系统级 API 请求与访问记录，保留原有分页与数据加载逻辑，并统一为第二阶段的中层页面结构。" })
    ] }) }),
    /* @__PURE__ */ r.jsxs("div", { className: "sh-section-grid", children: [
      /* @__PURE__ */ r.jsxs("div", { className: "sh-summary-card", children: [
        /* @__PURE__ */ r.jsx("span", { className: "sh-summary-card-label", children: "总记录数" }),
        /* @__PURE__ */ r.jsx("span", { className: "sh-summary-card-value", children: j }),
        /* @__PURE__ */ r.jsxs("span", { className: "sh-summary-card-meta", children: [
          "当前页 ",
          v.length,
          " 条"
        ] })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { className: "sh-meta-block", children: [
        /* @__PURE__ */ r.jsxs("div", { className: "sh-meta-row", children: [
          /* @__PURE__ */ r.jsx("span", { className: "sh-meta-label", children: "成功请求" }),
          /* @__PURE__ */ r.jsx("span", { className: "sh-meta-value", children: m })
        ] }),
        /* @__PURE__ */ r.jsxs("div", { className: "sh-meta-row", children: [
          /* @__PURE__ */ r.jsx("span", { className: "sh-meta-label", children: "失败请求" }),
          /* @__PURE__ */ r.jsx("span", { className: "sh-meta-value", children: E })
        ] }),
        /* @__PURE__ */ r.jsxs("div", { className: "sh-meta-row", children: [
          /* @__PURE__ */ r.jsx("span", { className: "sh-meta-label", children: "平均响应" }),
          /* @__PURE__ */ r.jsxs("span", { className: "sh-meta-value", children: [
            c,
            "ms"
          ] })
        ] })
      ] })
    ] }),
    /* @__PURE__ */ r.jsxs("div", { className: "sh-helper-block", children: [
      /* @__PURE__ */ r.jsx("h3", { className: "sh-helper-block-title", children: "查看说明" }),
      /* @__PURE__ */ r.jsx("p", { children: "第二阶段优先统一审计页面的信息密度与状态表达，筛选器与高级检索能力在后续按真实需求继续补充。" })
    ] }),
    /* @__PURE__ */ r.jsxs("section", { className: "sh-page-panel", children: [
      /* @__PURE__ */ r.jsxs("div", { className: "sh-page-panel-header", children: [
        /* @__PURE__ */ r.jsx("h2", { className: "sh-page-panel-title", children: "访问记录" }),
        /* @__PURE__ */ r.jsxs("div", { className: "sh-page-panel-meta", children: [
          "第 ",
          p,
          " 页"
        ] })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { className: "sh-page-panel-body", children: [
        /* @__PURE__ */ r.jsx("div", { className: "sh-table-wrap", children: /* @__PURE__ */ r.jsxs("table", { className: "sh-table", children: [
          /* @__PURE__ */ r.jsx("thead", { children: /* @__PURE__ */ r.jsxs("tr", { children: [
            /* @__PURE__ */ r.jsx("th", { children: "时间" }),
            /* @__PURE__ */ r.jsx("th", { children: "用户" }),
            /* @__PURE__ */ r.jsx("th", { children: "方法" }),
            /* @__PURE__ */ r.jsx("th", { children: "路径" }),
            /* @__PURE__ */ r.jsx("th", { children: "状态码" }),
            /* @__PURE__ */ r.jsx("th", { children: "响应时间" }),
            /* @__PURE__ */ r.jsx("th", { children: "IP" })
          ] }) }),
          /* @__PURE__ */ r.jsx("tbody", { children: v.map((o) => /* @__PURE__ */ r.jsxs("tr", { children: [
            /* @__PURE__ */ r.jsx("td", { children: new Date(o.accessTime).toLocaleString() }),
            /* @__PURE__ */ r.jsx("td", { children: o.userName || "-" }),
            /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx("span", { className: "sh-code", children: o.httpMethod }) }),
            /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx("span", { className: "sh-code", children: o.requestPath }) }),
            /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx(
              "span",
              {
                className: `sh-status-badge ${o.statusCode < 400 ? "sh-status-badge-success" : "sh-status-badge-danger"}`,
                children: o.statusCode
              }
            ) }),
            /* @__PURE__ */ r.jsxs("td", { children: [
              o.responseTime,
              "ms"
            ] }),
            /* @__PURE__ */ r.jsx("td", { children: o.clientIp || "-" })
          ] }, o.id)) })
        ] }) }),
        /* @__PURE__ */ r.jsxs("div", { className: "sh-pagination", children: [
          /* @__PURE__ */ r.jsxs("div", { className: "sh-pagination-info", children: [
            "共 ",
            j,
            " 条记录"
          ] }),
          /* @__PURE__ */ r.jsxs("div", { className: "sh-pagination-actions", children: [
            /* @__PURE__ */ r.jsx(
              "button",
              {
                className: "sh-btn",
                onClick: () => y((o) => Math.max(1, o - 1)),
                disabled: p === 1,
                children: "上一页"
              }
            ),
            /* @__PURE__ */ r.jsxs("span", { className: "sh-pagination-info", children: [
              "第 ",
              p,
              " / ",
              Math.ceil(j / d) || 1,
              " 页"
            ] }),
            /* @__PURE__ */ r.jsx(
              "button",
              {
                className: "sh-btn",
                onClick: () => y((o) => o + 1),
                disabled: p >= Math.ceil(j / d),
                children: "下一页"
              }
            )
          ] })
        ] })
      ] })
    ] })
  ] });
}
const gr = {
  metadata: {
    moduleId: "Audit",
    displayName: "审计日志",
    version: "1.0.0",
    description: "平台审计与日志管理模块"
  },
  routes: [
    {
      path: "/audit/operation-logs",
      component: pr,
      requireAuth: !0,
      permissions: ["Audit.OperationLog.View"]
    },
    {
      path: "/audit/access-logs",
      component: mr,
      requireAuth: !0,
      permissions: ["Audit.AccessLog.View"]
    }
  ],
  menus: [
    {
      id: "audit",
      label: "审计日志",
      icon: "document-text",
      order: 20,
      permissions: ["Audit.OperationLog.View"],
      children: [
        {
          id: "audit-operation-logs",
          label: "操作日志",
          path: "/audit/operation-logs",
          icon: "clipboard-list",
          order: 1,
          permissions: ["Audit.OperationLog.View"]
        },
        {
          id: "audit-access-logs",
          label: "访问日志",
          path: "/audit/access-logs",
          icon: "globe-alt",
          order: 2,
          permissions: ["Audit.AccessLog.View"]
        }
      ]
    }
  ],
  permissions: [
    { code: "Audit.OperationLog.View", name: "查看操作日志", category: "操作日志" },
    { code: "Audit.AccessLog.View", name: "查看访问日志", category: "访问日志" }
  ]
};
export {
  gr as default
};
