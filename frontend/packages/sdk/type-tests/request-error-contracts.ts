import { readStandardCrudRequestError, StandardCrudRequestError } from '../src';

const parseResponse: (response: Response, fallbackDiagnostic?: string) => Promise<StandardCrudRequestError> = readStandardCrudRequestError;
void parseResponse;

// @ts-expect-error 错误解析入口接受真实 Response，不接受另一份错误协议。
readStandardCrudRequestError({ code: 'AUTH_ADMISSION_DENIED' });

// @ts-expect-error 返回结构化请求错误，不压成最终提示字符串。
const parseText: (response: Response) => Promise<string> = readStandardCrudRequestError;
void parseText;

const unknownOutcome = new StandardCrudRequestError('diagnostic', 0, {}, {
  code: 'Platform.MutationOutcomeUnknown', reasonKey: 'Platform.MutationOutcomeUnknown',
}, undefined, new TypeError('network unavailable'));
const originalCause: unknown = unknownOutcome.cause;
void originalCause;
