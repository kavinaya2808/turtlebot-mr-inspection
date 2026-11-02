// Copyright 2026 Kavinaya
// SPDX-License-Identifier: Apache-2.0

#include <gtest/gtest.h>

#include <cmath>
#include <vector>

#include "tb3_mr_inspection/scan_resample.hpp"

using tb3_mr_inspection::resample_scan;

namespace
{
constexpr double kDeg = M_PI / 180.0;

std::vector<float> ramp(int n)
{
  std::vector<float> v(n);
  for (int i = 0; i < n; ++i) {
    v[i] = 1.0f + 0.01f * static_cast<float>(i);
  }
  return v;
}
}  // namespace

TEST(ScanResample, AlwaysProducesTheRequestedBeamCount)
{
  // The LDS-02 delivers anywhere from about 218 to 244 beams per revolution.
  for (int n : {218, 237, 244}) {
    const auto r = resample_scan(ramp(n), {}, 0.0, 2.0 * M_PI / n, 360);
    EXPECT_EQ(r.ranges.size(), 360u) << "input beams: " << n;
    EXPECT_NEAR(r.angle_increment, kDeg, 1e-12);
    EXPECT_DOUBLE_EQ(r.angle_min, 0.0);
  }
}

TEST(ScanResample, IdentityWhenInputAlreadyMatchesTheGrid)
{
  const auto in = ramp(360);
  const auto r = resample_scan(in, {}, 0.0, kDeg, 360);
  ASSERT_EQ(r.ranges.size(), 360u);
  for (int i = 0; i < 360; ++i) {
    EXPECT_FLOAT_EQ(r.ranges[i], in[i]) << "beam " << i;
  }
}

TEST(ScanResample, HandlesScansThatStartAtMinusPi)
{
  // Input starts at -pi: input beam 180 points straight ahead (0 rad).
  const auto in = ramp(360);
  const auto r = resample_scan(in, {}, -M_PI, kDeg, 360);
  EXPECT_FLOAT_EQ(r.ranges[0], in[180]);   // 0 deg
  EXPECT_FLOAT_EQ(r.ranges[90], in[270]);  // +90 deg
}

TEST(ScanResample, GapsBecomeInfinity)
{
  // Half a revolution only: 0..179 deg at 1 deg spacing.
  const auto in = ramp(180);
  const auto r = resample_scan(in, {}, 0.0, kDeg, 360);
  EXPECT_FLOAT_EQ(r.ranges[45], in[45]);
  EXPECT_TRUE(std::isinf(r.ranges[270]));
}

TEST(ScanResample, FullCircleWrapsAroundWithoutGaps)
{
  // 237 beams over a full revolution starting at -pi: no output beam may be left empty.
  const int n = 237;
  const auto r = resample_scan(ramp(n), {}, -M_PI, 2.0 * M_PI / n, 360);
  for (int i = 0; i < 360; ++i) {
    EXPECT_FALSE(std::isinf(r.ranges[i])) << "beam " << i;
  }
}

TEST(ScanResample, KeepsIntensitiesOnlyWhenTheyMatchTheRanges)
{
  const auto in = ramp(360);
  const auto with = resample_scan(in, in, 0.0, kDeg, 360);
  EXPECT_EQ(with.intensities.size(), 360u);
  const auto without = resample_scan(in, {1.0f, 2.0f}, 0.0, kDeg, 360);
  EXPECT_TRUE(without.intensities.empty());
}

TEST(ScanResample, RejectsInvalidInput)
{
  EXPECT_TRUE(resample_scan({}, {}, 0.0, kDeg, 360).ranges.empty());
  EXPECT_TRUE(resample_scan(ramp(10), {}, 0.0, 0.0, 360).ranges.empty());
  EXPECT_TRUE(resample_scan(ramp(10), {}, 0.0, kDeg, 0).ranges.empty());
}
