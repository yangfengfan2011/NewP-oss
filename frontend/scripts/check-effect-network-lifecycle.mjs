import { existsSync, readFileSync, readdirSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import ts from 'typescript';
import { effectNetworkLifecycleAllowlist } from './effect-network-lifecycle.allowlist.mjs';

const COMMAND_NAME_PATTERN = /^(create|update|delete|remove|action|enable|disable|set|reset|change|login|logout|save|submit|execute|activate|deactivate|restore)/i;
const READ_NAME_PATTERN = /^(get|list|load|read|fetch|query|search|find|lookup|request|export)/i;
const SHARED_LOADER_NAMES = new Set([
  'loadStandardCrudLookupOptions',
  'loadRuntimeLocalizationManifest',
  'loadRuntimeMetadata',
]);

function isPackageImport(moduleName, packageName) {
  return moduleName === packageName || moduleName.startsWith(`${packageName}/`);
}

function lineOf(sourceFile, node) {
  return sourceFile.getLineAndCharacterOfPosition(node.getStart(sourceFile)).line + 1;
}

function readEffectHookImports(sourceFile) {
  const aliases = new Map();
  const namespaces = new Set();
  for (const statement of sourceFile.statements) {
    if (!ts.isImportDeclaration(statement) || statement.moduleSpecifier.text !== 'react') continue;
    if (statement.importClause?.name) namespaces.add(statement.importClause.name.text);
    const bindings = statement.importClause?.namedBindings;
    if (bindings && ts.isNamespaceImport(bindings)) namespaces.add(bindings.name.text);
    if (bindings && ts.isNamedImports(bindings)) {
      for (const element of bindings.elements) {
        const importedName = element.propertyName?.text ?? element.name.text;
        if (importedName === 'useEffect' || importedName === 'useLayoutEffect') {
          aliases.set(element.name.text, importedName);
        }
      }
    }
  }
  return { aliases, namespaces };
}

function readImportedCallables(sourceFile) {
  const imports = new Map();
  for (const statement of sourceFile.statements) {
    if (!ts.isImportDeclaration(statement) || !ts.isStringLiteral(statement.moduleSpecifier)) continue;
    const source = statement.moduleSpecifier.text.replaceAll('\\', '/');
    const bindings = statement.importClause?.namedBindings;
    if (!bindings || !ts.isNamedImports(bindings)) continue;
    for (const element of bindings.elements) {
      imports.set(element.name.text, {
        importedName: element.propertyName?.text ?? element.name.text,
        source,
      });
    }
  }
  return imports;
}

function readPlatformQueryImports(sourceFile) {
  const aliases = new Set();
  const namespaces = new Set();
  for (const statement of sourceFile.statements) {
    if (!ts.isImportDeclaration(statement) || statement.moduleSpecifier.text !== '@icp-platform/core') continue;
    const bindings = statement.importClause?.namedBindings;
    if (bindings && ts.isNamespaceImport(bindings)) namespaces.add(bindings.name.text);
    if (bindings && ts.isNamedImports(bindings)) {
      for (const element of bindings.elements) {
        if ((element.propertyName?.text ?? element.name.text) === 'usePlatformQuery') {
          aliases.add(element.name.text);
        }
      }
    }
  }
  return { aliases, namespaces };
}

function isPlatformQueryHook(expression, platformQueryImports) {
  return ts.isIdentifier(expression)
    ? platformQueryImports.aliases.has(expression.text)
    : ts.isPropertyAccessExpression(expression)
      && ts.isIdentifier(expression.expression)
      && platformQueryImports.namespaces.has(expression.expression.text)
      && expression.name.text === 'usePlatformQuery';
}

function readPlatformQueryLoadCallback(call, platformQueryImports, localBindings, sourceFile) {
  if (!isPlatformQueryHook(call.expression, platformQueryImports)) return undefined;
  const options = call.arguments[0];
  if (!options || !ts.isObjectLiteralExpression(options)) return undefined;
  const loadProperty = options.properties.find((property) => (
    (ts.isPropertyAssignment(property) || ts.isShorthandPropertyAssignment(property) || ts.isMethodDeclaration(property))
    && ((ts.isIdentifier(property.name) || ts.isStringLiteralLike(property.name)) && property.name.text === 'load')
  ));
  if (!loadProperty) return undefined;
  if (ts.isMethodDeclaration(loadProperty) && loadProperty.body) {
    return { body: loadProperty.body, functionChain: [], visitedBindings: new Set() };
  }
  if (ts.isMethodDeclaration(loadProperty)) return undefined;
  const initializer = ts.isPropertyAssignment(loadProperty) ? loadProperty.initializer : loadProperty.name;
  if (ts.isArrowFunction(initializer) || ts.isFunctionExpression(initializer)) {
    return { body: initializer.body, functionChain: [], visitedBindings: new Set() };
  }
  if (ts.isIdentifier(initializer)) {
    const binding = resolveLocalBinding(initializer.text, call, localBindings, sourceFile);
    if (binding?.body) {
      return {
        body: binding.body,
        functionChain: [initializer.text],
        visitedBindings: new Set([binding.declaration]),
      };
    }
  }
  return undefined;
}

function classifyImportedCallable(imported) {
  if (!imported) return undefined;
  const apiOrServiceSource = /(^|\/)(api|services)(\/|$)/i.test(imported.source)
    || /(?:client|service)(?:\.[^/]*)?$/i.test(imported.source);
  const loaderSource = /loader(?:\.[^/]*)?$/i.test(imported.source);
  if (!apiOrServiceSource && !(loaderSource && /^load/i.test(imported.importedName))
    && !SHARED_LOADER_NAMES.has(imported.importedName)) return undefined;
  if (COMMAND_NAME_PATTERN.test(imported.importedName)) return 'write-command';
  if (READ_NAME_PATTERN.test(imported.importedName)) return 'read-query';
  return 'unknown-network';
}

function classifyTransportCall(call) {
  const input = call.arguments[0];
  const init = call.arguments[1];
  if (!init) {
    return input && (ts.isStringLiteralLike(input) || ts.isNoSubstitutionTemplateLiteral(input))
      ? 'read-query'
      : 'unknown-network';
  }
  if (!ts.isObjectLiteralExpression(init)) return 'unknown-network';
  const methodProperty = init.properties.find((property) => (
    (ts.isPropertyAssignment(property) || ts.isShorthandPropertyAssignment(property))
    && property.name.getText() === 'method'
  ));
  if (!methodProperty) {
    return init.properties.some(ts.isSpreadAssignment) ? 'unknown-network' : 'read-query';
  }
  if (!ts.isPropertyAssignment(methodProperty) || !ts.isStringLiteralLike(methodProperty.initializer)) {
    return 'unknown-network';
  }
  const method = methodProperty.initializer.text.toUpperCase();
  if (method === 'GET' || method === 'HEAD') return 'read-query';
  if (method === 'POST' || method === 'PUT' || method === 'PATCH' || method === 'DELETE') return 'write-command';
  return 'unknown-network';
}

function classifyMemberName(receiver, method) {
  const isDataAdapter = receiver === 'dataAdapter' || receiver.endsWith('.dataAdapter');
  const isClientOrService = /(?:client|service)$/i.test(receiver);
  if (isDataAdapter || isClientOrService) {
    if (COMMAND_NAME_PATTERN.test(method)) return 'write-command';
    if (READ_NAME_PATTERN.test(method)) return 'read-query';
    return 'unknown-network';
  }
  if (/(?:^|\.)[^.]*provider$/i.test(receiver) && method === 'load') return 'read-query';
  return undefined;
}

function classifyMemberSink(expression, sourceFile) {
  if (!ts.isPropertyAccessExpression(expression)) return undefined;
  return classifyMemberName(expression.expression.getText(sourceFile), expression.name.text);
}

function enclosingFunctionName(node) {
  let current = node.parent;
  while (current) {
    if (ts.isFunctionDeclaration(current) && current.name) return current.name.text;
    if (ts.isVariableDeclaration(current) && ts.isIdentifier(current.name)) return current.name.text;
    current = current.parent;
  }
  return '<module>';
}

function isFunctionNode(node) {
  return ts.isFunctionDeclaration(node)
    || ts.isFunctionExpression(node)
    || ts.isArrowFunction(node)
    || ts.isMethodDeclaration(node);
}

function containingScope(node, sourceFile) {
  let current = node.parent;
  while (current && current !== sourceFile) {
    if (isFunctionNode(current)) return current;
    current = current.parent;
  }
  return sourceFile;
}

function collectLocalBindings(sourceFile) {
  const bindings = new Map();
  function add(name, declaration, body, memberAlias) {
    const candidates = bindings.get(name) ?? [];
    candidates.push({ declaration, body, memberAlias, scope: containingScope(declaration, sourceFile) });
    bindings.set(name, candidates);
  }
  function visit(node) {
    if (ts.isFunctionDeclaration(node) && node.name && node.body) {
      add(node.name.text, node, node.body);
    } else if (ts.isVariableDeclaration(node) && ts.isIdentifier(node.name) && node.initializer) {
      if (ts.isArrowFunction(node.initializer) || ts.isFunctionExpression(node.initializer)) {
        add(node.name.text, node, node.initializer.body);
      } else if (
        ts.isCallExpression(node.initializer)
        && ts.isIdentifier(node.initializer.expression)
        && node.initializer.expression.text === 'useCallback'
      ) {
        const callback = node.initializer.arguments[0];
        if (callback && (ts.isArrowFunction(callback) || ts.isFunctionExpression(callback))) {
          add(node.name.text, node, callback.body);
        }
      } else if (
        ts.isPropertyAccessExpression(node.initializer)
        && classifyMemberSink(node.initializer, sourceFile)
      ) {
        add(node.name.text, node, undefined, {
          text: node.initializer.getText(sourceFile),
          classification: classifyMemberSink(node.initializer, sourceFile),
        });
      }
    } else if (ts.isVariableDeclaration(node) && ts.isObjectBindingPattern(node.name) && node.initializer) {
      const receiver = node.initializer.getText(sourceFile);
      for (const element of node.name.elements) {
        if (!ts.isIdentifier(element.name)) continue;
        const method = element.propertyName && (ts.isIdentifier(element.propertyName) || ts.isStringLiteralLike(element.propertyName))
          ? element.propertyName.text
          : element.name.text;
        const classification = classifyMemberName(receiver, method);
        if (classification) {
          add(element.name.text, node, undefined, {
            text: `${receiver}.${method}`,
            classification,
          });
        }
      }
    }
    ts.forEachChild(node, visit);
  }
  visit(sourceFile);
  return bindings;
}

function resolveLocalBinding(name, callSite, bindings, sourceFile) {
  const candidates = bindings.get(name) ?? [];
  let scope = callSite;
  while (scope) {
    const candidate = candidates.find((item) => item.scope === scope);
    if (candidate) return candidate;
    if (scope === sourceFile) break;
    scope = containingScope(scope, sourceFile);
  }
  return candidates.find((item) => item.scope === sourceFile);
}

export function analyzeEffectNetworkLifecycleSource({ file, source }) {
  const sourceFile = ts.createSourceFile(
    file,
    source,
    ts.ScriptTarget.Latest,
    true,
    file.endsWith('.tsx') ? ts.ScriptKind.TSX : ts.ScriptKind.TS,
  );
  const effectImports = readEffectHookImports(sourceFile);
  const importedCallables = readImportedCallables(sourceFile);
  const platformQueryImports = readPlatformQueryImports(sourceFile);
  const localBindings = collectLocalBindings(sourceFile);
  const rawFindings = new Map();

  function recordFinding(node, triggerChain, classification, owner, hook) {
    const finding = {
      id: `${file}::${owner}::${hook}::${triggerChain}`,
      file,
      enclosingFunction: owner,
      hook,
      triggerChain,
      classification,
      line: lineOf(sourceFile, node),
    };
    rawFindings.set(`${finding.id}::${node.pos}`, finding);
  }

  function visit(node) {
    if (ts.isCallExpression(node)) {
      const hook = ts.isIdentifier(node.expression)
        ? effectImports.aliases.get(node.expression.text)
        : ts.isPropertyAccessExpression(node.expression)
          && ts.isIdentifier(node.expression.expression)
          && effectImports.namespaces.has(node.expression.expression.text)
          && (node.expression.name.text === 'useEffect' || node.expression.name.text === 'useLayoutEffect')
          ? node.expression.name.text
          : undefined;
      const callback = node.arguments[0];
      if (hook && callback && (ts.isArrowFunction(callback) || ts.isFunctionExpression(callback))) {
        const owner = enclosingFunctionName(node);
        inspectEffect(callback.body, [], owner, hook, new Set());
      } else if (hook && callback && ts.isIdentifier(callback)) {
        const owner = enclosingFunctionName(node);
        const binding = resolveLocalBinding(callback.text, node, localBindings, sourceFile);
        if (binding) inspectEffect(binding.body, [callback.text], owner, hook, new Set([binding.declaration]));
      }
      const queryLoad = readPlatformQueryLoadCallback(node, platformQueryImports, localBindings, sourceFile);
      if (queryLoad) {
        inspectEffect(
          queryLoad.body,
          queryLoad.functionChain,
          enclosingFunctionName(node),
          'usePlatformQuery.load',
          queryLoad.visitedBindings,
        );
      }
    }
    ts.forEachChild(node, visit);
  }

  function inspectEffect(node, functionChain, owner, hook, visitedBindings) {
    if (ts.isFunctionDeclaration(node) && node.name) {
      functionChain = [...functionChain, node.name.text];
    }
    const identifierClassification = ts.isCallExpression(node) && ts.isIdentifier(node.expression)
      ? node.expression.text === 'platformFetch' || node.expression.text === 'fetch'
        ? classifyTransportCall(node)
        : node.expression.text === 'request'
          ? 'read-query'
          : importedCallables.get(node.expression.text)?.importedName === 'platformFetch'
            ? classifyTransportCall(node)
            : classifyImportedCallable(importedCallables.get(node.expression.text))
      : undefined;
    const memberClassification = ts.isCallExpression(node)
      ? ts.isPropertyAccessExpression(node.expression)
        && (node.expression.expression.getText(sourceFile) === 'globalThis'
          || node.expression.expression.getText(sourceFile) === 'window')
        && node.expression.name.text === 'fetch'
        ? classifyTransportCall(node)
        : classifyMemberSink(node.expression, sourceFile)
      : undefined;
    if (ts.isCallExpression(node) && ts.isIdentifier(node.expression) && identifierClassification) {
      const triggerChain = [...functionChain, node.expression.text].join('>');
      recordFinding(node, triggerChain, identifierClassification, owner, hook);
    } else if (ts.isCallExpression(node) && ts.isPropertyAccessExpression(node.expression) && memberClassification) {
      const triggerChain = [...functionChain, node.expression.getText(sourceFile)].join('>');
      recordFinding(node, triggerChain, memberClassification, owner, hook);
    } else if (ts.isCallExpression(node) && ts.isIdentifier(node.expression)) {
      const binding = resolveLocalBinding(node.expression.text, node, localBindings, sourceFile);
      if (binding?.memberAlias) {
        const triggerChain = [
          ...functionChain,
          `${node.expression.text}(${binding.memberAlias.text})`,
        ].join('>');
        recordFinding(node, triggerChain, binding.memberAlias.classification, owner, hook);
      } else if (binding?.body && !visitedBindings.has(binding.declaration)) {
        inspectEffect(
          binding.body,
          [...functionChain, node.expression.text],
          owner,
          hook,
          new Set([...visitedBindings, binding.declaration]),
        );
      }
    }
    ts.forEachChild(node, (child) => inspectEffect(child, functionChain, owner, hook, visitedBindings));
  }

  visit(sourceFile);
  const groups = new Map();
  for (const finding of rawFindings.values()) {
    const group = groups.get(finding.id) ?? [];
    group.push(finding);
    groups.set(finding.id, group);
  }
  return [...groups.values()].flatMap((group) => (
    group.length === 1
      ? group
      : group.map((finding, index) => {
        const triggerChain = `${finding.triggerChain}#${index + 1}`;
        return {
          ...finding,
          id: `${finding.file}::${finding.enclosingFunction}::${finding.hook}::${triggerChain}`,
          triggerChain,
        };
      })
  ));
}

export function evaluateEffectNetworkLifecycle({ findings, allowlist }) {
  const diagnostics = [];
  const requiredAllowlistFields = [
    'id',
    'file',
    'enclosingFunction',
    'hook',
    'triggerChain',
    'classification',
    'owner',
    'reason',
    'exitCondition',
  ];
  const seenAllowlistIds = new Set();
  for (const entry of allowlist) {
    if (seenAllowlistIds.has(entry.id)) {
      diagnostics.push({ code: 'EFFECT_NETWORK_ALLOWLIST_DUPLICATE', id: entry.id });
    }
    seenAllowlistIds.add(entry.id);
  }
  for (const entry of allowlist) {
    const missingFields = requiredAllowlistFields.filter((field) => (
      typeof entry[field] !== 'string' || entry[field].trim().length === 0
    ));
    if (missingFields.length > 0) {
      diagnostics.push({ code: 'EFFECT_NETWORK_ALLOWLIST_INVALID', id: entry.id, missingFields });
    }
  }
  const allowlistById = new Map(allowlist.map((entry) => [entry.id, entry]));
  const isExactMatch = (entry, finding) => entry
    && entry.file === finding.file
    && entry.enclosingFunction === finding.enclosingFunction
    && entry.hook === finding.hook
    && entry.triggerChain === finding.triggerChain
    && entry.classification === finding.classification;
  for (const finding of findings) {
    if (finding.hook === 'usePlatformQuery.load') {
      if (finding.classification === 'write-command') {
        diagnostics.push({ code: 'PLATFORM_QUERY_LOAD_WRITE_COMMAND', id: finding.id });
      } else if (finding.classification === 'unknown-network') {
        diagnostics.push({ code: 'PLATFORM_QUERY_LOAD_UNCLASSIFIED_NETWORK', id: finding.id });
      }
      continue;
    }
    if (finding.classification === 'write-command') {
      diagnostics.push({ code: 'EFFECT_NETWORK_WRITE_COMMAND', id: finding.id });
      continue;
    }
    const entry = allowlistById.get(finding.id);
    if (!isExactMatch(entry, finding)) {
      diagnostics.push({ code: 'EFFECT_NETWORK_NOT_ALLOWLISTED', id: finding.id });
    }
  }
  for (const entry of allowlist) {
    if (entry.hook === 'usePlatformQuery.load') {
      diagnostics.push({ code: 'EFFECT_NETWORK_ALLOWLIST_QUERY_LOAD_FORBIDDEN', id: entry.id });
    }
    if (entry.classification === 'write-command') {
      diagnostics.push({ code: 'EFFECT_NETWORK_ALLOWLIST_WRITE_FORBIDDEN', id: entry.id });
    }
    if (!findings.some((finding) => finding.id === entry.id && isExactMatch(entry, finding))) {
      diagnostics.push({ code: 'EFFECT_NETWORK_ALLOWLIST_STALE', id: entry.id });
    }
  }
  return { ok: diagnostics.length === 0, diagnostics };
}

function collectProductionTypeScriptFiles(frontendRoot) {
  const files = [];
  const excludedDirectories = new Set(['node_modules', 'dist', 'coverage', '__tests__', '.vite']);
  function walk(directory) {
    if (!existsSync(directory)) return;
    for (const entry of readdirSync(directory, { withFileTypes: true })) {
      const target = path.join(directory, entry.name);
      if (entry.isDirectory()) {
        if (!excludedDirectories.has(entry.name)) walk(target);
      } else if (
        entry.isFile()
        && /\.(ts|tsx)$/.test(entry.name)
        && !/\.d\.ts$|\.(test|spec)\.(ts|tsx)$/.test(entry.name)
      ) {
        files.push(target);
      }
    }
  }
  for (const root of ['packages', 'modules', 'templates']) walk(path.join(frontendRoot, root));
  files.sort((left, right) => left.localeCompare(right));
  return files;
}

export function analyzeEffectNetworkLifecycleTree({ frontendRoot }) {
  return collectProductionTypeScriptFiles(frontendRoot).flatMap((absoluteFile) => analyzeEffectNetworkLifecycleSource({
    file: path.relative(frontendRoot, absoluteFile).replaceAll('\\', '/'),
    source: readFileSync(absoluteFile, 'utf8'),
  }));
}

function unwrapStaticExpression(expression) {
  let current = expression;
  while (
    ts.isParenthesizedExpression(current)
    || ts.isAsExpression(current)
    || ts.isTypeAssertionExpression(current)
    || ts.isNonNullExpression(current)
    || ts.isSatisfiesExpression(current)
  ) {
    current = current.expression;
  }
  return current;
}

function isDefinitelyNullishExpression(expression) {
  const current = unwrapStaticExpression(expression);
  if (current.kind === ts.SyntaxKind.NullKeyword || ts.isVoidExpression(current)) return true;
  if (ts.isIdentifier(current)) return current.text === 'undefined';
  if (ts.isConditionalExpression(current)) {
    return isDefinitelyNullishExpression(current.whenTrue)
      && isDefinitelyNullishExpression(current.whenFalse);
  }
  return false;
}

function isDefinitelyTruthyExpression(expression) {
  const current = unwrapStaticExpression(expression);
  if (current.kind === ts.SyntaxKind.TrueKeyword) return true;
  if (ts.isStringLiteral(current) || ts.isNoSubstitutionTemplateLiteral(current)) {
    return current.text.length > 0;
  }
  if (ts.isNumericLiteral(current)) return Number(current.text) !== 0;
  if (
    ts.isObjectLiteralExpression(current)
    || ts.isArrayLiteralExpression(current)
    || ts.isArrowFunction(current)
    || ts.isFunctionExpression(current)
    || ts.isClassExpression(current)
    || ts.isNewExpression(current)
    || ts.isRegularExpressionLiteral(current)
  ) return true;
  if (ts.isTemplateExpression(current)) {
    return current.head.text.length > 0
      || current.templateSpans.some(({ literal }) => literal.text.length > 0);
  }
  if (ts.isPrefixUnaryExpression(current) && current.operator === ts.SyntaxKind.ExclamationToken) {
    return isDefinitelyNullishExpression(current.operand)
      || current.operand.kind === ts.SyntaxKind.FalseKeyword;
  }
  if (ts.isConditionalExpression(current)) {
    return isDefinitelyTruthyExpression(current.whenTrue)
      && isDefinitelyTruthyExpression(current.whenFalse);
  }
  if (ts.isBinaryExpression(current) && current.operatorToken.kind === ts.SyntaxKind.BarBarToken) {
    return isDefinitelyTruthyExpression(current.left) || isDefinitelyTruthyExpression(current.right);
  }
  if (ts.isBinaryExpression(current) && current.operatorToken.kind === ts.SyntaxKind.QuestionQuestionToken) {
    return isDefinitelyTruthyExpression(current.left)
      || (isDefinitelyNullishExpression(current.left) && isDefinitelyTruthyExpression(current.right));
  }
  return false;
}

function isDefinitelyNonUndefinedExpression(expression) {
  const current = unwrapStaticExpression(expression);
  if (ts.isIdentifier(current)) return false;
  if (ts.isVoidExpression(current)) return false;
  if (
    current.kind === ts.SyntaxKind.NullKeyword
    || current.kind === ts.SyntaxKind.TrueKeyword
    || current.kind === ts.SyntaxKind.FalseKeyword
    || ts.isStringLiteral(current)
    || ts.isNoSubstitutionTemplateLiteral(current)
    || ts.isTemplateExpression(current)
    || ts.isNumericLiteral(current)
    || ts.isObjectLiteralExpression(current)
    || ts.isArrayLiteralExpression(current)
    || ts.isArrowFunction(current)
    || ts.isFunctionExpression(current)
    || ts.isClassExpression(current)
    || ts.isNewExpression(current)
    || ts.isRegularExpressionLiteral(current)
    || ts.isPrefixUnaryExpression(current)
    || ts.isTypeOfExpression(current)
    || ts.isDeleteExpression(current)
  ) return true;
  if (ts.isConditionalExpression(current)) {
    return isDefinitelyNonUndefinedExpression(current.whenTrue)
      && isDefinitelyNonUndefinedExpression(current.whenFalse);
  }
  if (ts.isBinaryExpression(current) && current.operatorToken.kind === ts.SyntaxKind.QuestionQuestionToken) {
    return isDefinitelyTruthyExpression(current.left)
      || isDefinitelyNonUndefinedExpression(current.right);
  }
  if (ts.isBinaryExpression(current) && current.operatorToken.kind === ts.SyntaxKind.BarBarToken) {
    return isDefinitelyTruthyExpression(current.left)
      || isDefinitelyNonUndefinedExpression(current.right);
  }
  return false;
}

function jsxAttributeDefinitelyProvides(attributes, attributeName, expressionPredicate) {
  let definitelyProvides = false;
  for (const attribute of attributes.properties) {
    if (ts.isJsxSpreadAttribute(attribute)) {
      // 后置 spread 可能把已确认的 prop 覆盖为 undefined；只有后续显式值才能重新建立证明。
      definitelyProvides = false;
      continue;
    }
    if (!ts.isJsxAttribute(attribute) || attribute.name.getText() !== attributeName) continue;
    if (!attribute.initializer) {
      // JSX shorthand 在运行时明确传入 true；shape 合法性继续由 TypeScript 负责。
      definitelyProvides = true;
    } else if (ts.isStringLiteral(attribute.initializer)) {
      definitelyProvides = expressionPredicate(attribute.initializer);
    } else {
      definitelyProvides = attribute.initializer.expression
        ? expressionPredicate(attribute.initializer.expression)
        : false;
    }
  }
  return definitelyProvides;
}

export function analyzeCompatibilityOnlyReAdoptionsSource({ file, source }) {
  const sourceFile = ts.createSourceFile(
    file,
    source,
    ts.ScriptTarget.Latest,
    true,
    file.endsWith('.tsx') ? ts.ScriptKind.TSX : ts.ScriptKind.TS,
  );
  const legacyLookupAliases = new Set();
  const sdkNamespaces = new Set();
  const tableAliases = new Set();
  const uiNamespaces = new Set();
  const authorizationProviderAliases = new Set();
  const coreNamespaces = new Set();
  for (const statement of sourceFile.statements) {
    if (!ts.isImportDeclaration(statement) || !ts.isStringLiteral(statement.moduleSpecifier)) continue;
    const moduleName = statement.moduleSpecifier.text;
    const bindings = statement.importClause?.namedBindings;
    if (isPackageImport(moduleName, '@icp-platform/sdk') && bindings && ts.isNamespaceImport(bindings)) {
      sdkNamespaces.add(bindings.name.text);
    }
    if (isPackageImport(moduleName, '@icp-platform/sdk') && bindings && ts.isNamedImports(bindings)) {
      for (const element of bindings.elements) {
        if ((element.propertyName?.text ?? element.name.text) === 'useStandardCrudLookupRuntime') {
          legacyLookupAliases.add(element.name.text);
        }
      }
    }
    if (isPackageImport(moduleName, '@icp-platform/ui') && bindings && ts.isNamespaceImport(bindings)) {
      uiNamespaces.add(bindings.name.text);
    }
    if (isPackageImport(moduleName, '@icp-platform/ui') && bindings && ts.isNamedImports(bindings)) {
      for (const element of bindings.elements) {
        const importedName = element.propertyName?.text ?? element.name.text;
        if (importedName === 'SearchTablePage' || importedName === 'CrudTableSection') {
          tableAliases.add(element.name.text);
        }
      }
    }
    if (isPackageImport(moduleName, '@icp-platform/core') && bindings && ts.isNamespaceImport(bindings)) {
      coreNamespaces.add(bindings.name.text);
    }
    if (isPackageImport(moduleName, '@icp-platform/core') && bindings && ts.isNamedImports(bindings)) {
      for (const element of bindings.elements) {
        if ((element.propertyName?.text ?? element.name.text) === 'AuthorizationProvider') {
          authorizationProviderAliases.add(element.name.text);
        }
      }
    }
  }

  const diagnostics = [];
  function visit(node) {
    if (ts.isCallExpression(node)) {
      const isLegacyLookupCall = ts.isIdentifier(node.expression)
        ? legacyLookupAliases.has(node.expression.text)
        : ts.isPropertyAccessExpression(node.expression)
          && ts.isIdentifier(node.expression.expression)
          && sdkNamespaces.has(node.expression.expression.text)
          && node.expression.name.text === 'useStandardCrudLookupRuntime';
      if (isLegacyLookupCall) {
        diagnostics.push({
          code: 'COMPATIBILITY_ONLY_SDK_LOOKUP_RUNTIME_READOPTED',
          id: `${file}::${enclosingFunctionName(node)}::useStandardCrudLookupRuntime`,
          line: lineOf(sourceFile, node),
        });
      }
    }
    if (ts.isJsxOpeningElement(node) || ts.isJsxSelfClosingElement(node)) {
      const tagName = node.tagName;
      const isCompatibilityTable = ts.isIdentifier(tagName)
        ? tableAliases.has(tagName.text)
        : ts.isPropertyAccessExpression(tagName)
          && ts.isIdentifier(tagName.expression)
          && uiNamespaces.has(tagName.expression.text)
          && (tagName.name.text === 'SearchTablePage' || tagName.name.text === 'CrudTableSection');
      const isControlled = jsxAttributeDefinitelyProvides(
        node.attributes,
        'controlledResult',
        isDefinitelyTruthyExpression,
      );
      if (isCompatibilityTable && !isControlled) {
        diagnostics.push({
          code: 'COMPATIBILITY_ONLY_SEARCH_TABLE_UNCONTROLLED_READOPTED',
          id: `${file}::${enclosingFunctionName(node)}::${tagName.getText(sourceFile)}`,
          line: lineOf(sourceFile, node),
        });
      }
      const isAuthorizationProvider = ts.isIdentifier(tagName)
        ? authorizationProviderAliases.has(tagName.text)
        : ts.isPropertyAccessExpression(tagName)
          && ts.isIdentifier(tagName.expression)
          && coreNamespaces.has(tagName.expression.text)
          && tagName.name.text === 'AuthorizationProvider';
      const hasControlledPermissions = jsxAttributeDefinitelyProvides(
        node.attributes,
        'permissions',
        isDefinitelyNonUndefinedExpression,
      );
      if (isAuthorizationProvider && !hasControlledPermissions) {
        diagnostics.push({
          code: 'COMPATIBILITY_ONLY_AUTHORIZATION_UNCONTROLLED_READOPTED',
          id: `${file}::${enclosingFunctionName(node)}::${tagName.getText(sourceFile)}`,
          line: lineOf(sourceFile, node),
        });
      }
    }
    ts.forEachChild(node, visit);
  }
  visit(sourceFile);
  return diagnostics;
}

export function analyzeCompatibilityOnlyReAdoptionsTree({ frontendRoot }) {
  return collectProductionTypeScriptFiles(frontendRoot).flatMap((absoluteFile) => analyzeCompatibilityOnlyReAdoptionsSource({
    file: path.relative(frontendRoot, absoluteFile).replaceAll('\\', '/'),
    source: readFileSync(absoluteFile, 'utf8'),
  }));
}

export function runEffectNetworkLifecycleGate({ frontendRoot, allowlist = effectNetworkLifecycleAllowlist }) {
  const findings = analyzeEffectNetworkLifecycleTree({ frontendRoot });
  const evaluation = evaluateEffectNetworkLifecycle({ findings, allowlist });
  const compatibilityDiagnostics = analyzeCompatibilityOnlyReAdoptionsTree({ frontendRoot });
  const diagnostics = [...evaluation.diagnostics, ...compatibilityDiagnostics];
  return { ok: diagnostics.length === 0, diagnostics, findings, compatibilityDiagnostics };
}

const invokedPath = process.argv[1] ? pathToFileURL(path.resolve(process.argv[1])).href : undefined;
if (invokedPath === import.meta.url) {
  const frontendRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
  const result = runEffectNetworkLifecycleGate({ frontendRoot });
  const writeCount = result.findings.filter(({ classification }) => classification === 'write-command').length;
  const queryLoadCount = result.findings.filter(({ hook }) => hook === 'usePlatformQuery.load').length;
  const effectFindingCount = result.findings.length - queryLoadCount;
  if (result.ok) {
    console.log(`[effect-network-lifecycle] ${effectFindingCount} reviewed effect findings, ${queryLoadCount} platform query loads, ${writeCount} write commands`);
  } else {
    for (const diagnostic of result.diagnostics) {
      console.error(`[effect-network-lifecycle] ${diagnostic.code}: ${diagnostic.id}`);
    }
    process.exitCode = 1;
  }
}
