import { describe, it, expect } from 'vitest';

const { versionBadge } = await import('../src/shell.js');

/**
 * The sidebar version badge.
 *
 * It rendered **`vv0.9.0-1-g7fb71c0`** the moment the server started reporting the git tag instead of
 * a bare assembly version: the badge hardcoded a `v` prefix, and the value now brings its own. Two
 * shapes reach it and both have to read right.
 */
describe('versionBadge', () => {
  it('does not double the v when the value already carries one', () => {
    // The bug, exactly as it appeared on screen.
    expect(versionBadge('v0.9.0-1-g7fb71c0')).toBe('v0.9.0-1-g7fb71c0');
    expect(versionBadge('V0.9.0')).toBe('v0.9.0');
  });

  it('adds the v when the value has none', () => {
    expect(versionBadge('0.9.0-1-g7fb71c0')).toBe('v0.9.0-1-g7fb71c0');
    expect(versionBadge('1.0.0')).toBe('v1.0.0');
  });

  it('keeps a git-describe string INTACT', () => {
    // The commit distance and sha are the whole reason this is more useful than a bare tag. The old
    // blanket `.split('.').slice(0,3)` would have eaten them from any describe string with more dots.
    expect(versionBadge('v0.9.0-1-g7fb71c0')).toBe('v0.9.0-1-g7fb71c0');
    expect(versionBadge('v0.10.2-14-gdeadbee-dirty')).toBe('v0.10.2-14-gdeadbee-dirty');
  });

  it('drops the meaningless fourth part of a bare assembly version', () => {
    // The behaviour the old code existed for, kept: 0.9.0.0 -> v0.9.0.
    expect(versionBadge('0.9.0.0')).toBe('v0.9.0');
    expect(versionBadge('0.4.11.0')).toBe('v0.4.11');
  });

  it('leaves a three-part version alone', () => {
    expect(versionBadge('0.9.0')).toBe('v0.9.0');
  });

  it('renders nothing rather than a bare v when there is no version', () => {
    // A lone "v" in the sidebar looks like a rendering fault; the placeholder should stay instead.
    for (const empty of ['', '   ', null, undefined]) expect(versionBadge(empty)).toBe('');
  });
});
