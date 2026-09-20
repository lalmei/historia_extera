import { test } from 'node:test';
import assert from 'node:assert/strict';
import { bandMotion } from './types.ts';

/**
 * The orientation line used to print "band 27° to the horizon", which was three things wrong at
 * once: the number was the complement of the angle its name claimed, the export has no observer
 * or sidereal time with which to find a horizon, and the sense was inverted — the value ran
 * highest exactly when the band moves least. What a reader wanted from it was what the band does
 * over a night, so that is what the line says now, and these are its edges.
 */
test('the band moves less the closer the spin axis is to the galactic axis', () => {
  // Axis on the galactic axis: the galactic plane is the celestial equator, so a night's turning
  // carries the band around within itself and it never leaves its altitude.
  assert.equal(bandMotion(0), 'the band holds its place through the night');
  assert.equal(bandMotion(24.9), 'the band holds its place through the night');

  // Earth's 63° sits in the middle band, where the sky visibly swings the Milky Way about.
  assert.equal(bandMotion(25), 'the night swings the band across the sky');
  assert.equal(bandMotion(63), 'the night swings the band across the sky');

  // Axis lying in the band: the band contains the celestial pole and sweeps the whole sky.
  assert.equal(bandMotion(65), 'the night carries the band from pole to pole');
  assert.equal(bandMotion(90), 'the night carries the band from pole to pole');
});

test('no reading of the tilt describes an angle to a horizon', () => {
  for (let tilt = 0; tilt <= 90; tilt += 1) {
    const reading = bandMotion(tilt);
    assert.ok(reading.length > 0, `tilt ${tilt} should read as something`);
    assert.ok(
      !/horizon/i.test(reading),
      `tilt ${tilt} must not claim a horizon the export cannot place: ${reading}`,
    );
  }
});
