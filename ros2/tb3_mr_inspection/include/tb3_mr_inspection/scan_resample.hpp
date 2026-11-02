// Copyright 2026 Kavinaya
// SPDX-License-Identifier: Apache-2.0
//
// Resamples a 2D laser scan with a varying beam count onto a fixed angular grid.
//
// The TurtleBot3 LDS-02 returns a different number of beams in every scan (about 218-244).
// slam_toolbox locks onto the first scan's length and rejects every later scan of another
// length ("LaserRangeScan contains N range readings, expected M"), so the map never grows.
// Resampling every scan onto the same 360 x 1-degree grid fixes that.

#pragma once

#include <algorithm>
#include <cmath>
#include <limits>
#include <vector>

namespace tb3_mr_inspection
{

struct ResampledScan
{
  std::vector<float> ranges;
  std::vector<float> intensities;  // empty when the input carries no intensities
  double angle_min = 0.0;
  double angle_increment = 0.0;
  double angle_max = 0.0;
};

/// Map every output beam (angle i * 2pi/beams, i = 0..beams-1) to the nearest input beam.
/// Output beams that fall in a gap of the input scan (more than one input increment away
/// from any input beam) are set to +inf, which slam_toolbox treats as "no return".
inline ResampledScan resample_scan(
  const std::vector<float> & ranges,
  const std::vector<float> & intensities,
  double angle_min,
  double angle_increment,
  int beams)
{
  ResampledScan out;
  const auto k = static_cast<long>(ranges.size());
  if (k == 0 || angle_increment <= 0.0 || beams <= 0) {
    return out;
  }

  constexpr double kTwoPi = 2.0 * M_PI;
  const double inc = kTwoPi / beams;
  const bool has_intensities = intensities.size() == ranges.size();

  out.ranges.assign(beams, std::numeric_limits<float>::infinity());
  if (has_intensities) {
    out.intensities.assign(beams, 0.0f);
  }

  // The input covers [0, last] relative to its first beam. An output beam more than one input
  // increment past the last beam, and more than one increment before wrapping around to the
  // first beam, lies in a coverage gap.
  const double last = static_cast<double>(k - 1) * angle_increment;

  for (int i = 0; i < beams; ++i) {
    // Angle of the output beam, measured from the input scan's first beam, in [0, 2pi).
    double rel = std::fmod(i * inc - angle_min, kTwoPi);
    if (rel < 0.0) {
      rel += kTwoPi;
    }
    const bool in_gap = rel > last + angle_increment && (kTwoPi - rel) > angle_increment;
    if (in_gap) {
      continue;  // stays +inf (and intensity 0)
    }
    // Past the halfway point to the wrap-around, the first beam is the nearest one.
    const long idx = std::lround(rel / angle_increment);
    const long j = rel > last + 0.5 * angle_increment ? 0 : std::min(idx, k - 1);

    out.ranges[i] = ranges[j];
    if (has_intensities) {
      out.intensities[i] = intensities[j];
    }
  }

  out.angle_min = 0.0;
  out.angle_increment = inc;
  out.angle_max = inc * (beams - 1);
  return out;
}

}  // namespace tb3_mr_inspection
