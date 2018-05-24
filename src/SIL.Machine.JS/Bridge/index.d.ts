﻿export interface ProgressStatus {
    readonly percentCompleted: number;
    readonly message: string;
}

export enum TrainResultCode {
    noError,
    httpError,
    trainError
}

export interface InteractiveTranslationSession {
    readonly sourceSegment: string[];
    confidenceThreshold: number;
    readonly prefix: string[];
    readonly isLastWordComplete: boolean;
    readonly suggestion: string[];
    readonly suggestionConfidence: number;
    readonly isInitialized: boolean;
    readonly isSourceSegmentValid: boolean;
    initialize(): void;
    updatePrefix(prefix: string): string[];
    getSuggestionText(suggestionIndex?: number): string;
    updateSuggestion(): void;
    approve(onFinished: { (arg: boolean): void }): void;
}

export class TranslationEngine {
    constructor(baseUrl: string, projectId: string);
    translateInteractively(sourceSegment: string, confidenceThreshold: number,
        onFinished: { (arg: InteractiveTranslationSession): void }): void;
    train(onStatusUpdate: { (arg: ProgressStatus): void },
        onFinished: { (arg: TrainResultCode): void }): void;
    startTraining(onFinished: { (arg: boolean): void }): void;
    listenForTrainingStatus(onStatusUpdate: { (arg: ProgressStatus): void },
        onFinished: { (arg: TrainResultCode): void }): void;
    getConfidence(onFinished: { (success: boolean, confidence: number): void }): void;
    close(): void;
}

export interface Range {
    start: number;
    end: number;
    length: number;
}

export class SegmentTokenizer {
    constructor(segmentType: string);
    tokenize(text: string, index?: number, length?: number): Range[];
    tokenizeToStrings(text: string, index?: number, length?: number): string[];
}
