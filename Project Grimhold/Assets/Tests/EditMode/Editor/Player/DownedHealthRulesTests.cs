using NUnit.Framework;

namespace Tests.EditMode.Player
{
    public sealed class DownedHealthRulesTests
    {
        [Test]
        public void TryCreateReserve_StartsAtFullConfiguredValue()
        {
            bool created = DownedHealthRules.TryCreateReserve(75f, out float reserve);

            Assert.That(created, Is.True);
            Assert.That(reserve, Is.EqualTo(75f));
        }

        [Test]
        public void TryCreateReserve_IsIndependentFromMaximumHealth()
        {
            DownedHealthRules.TryCreateReserve(75f, out float first);
            DownedHealthRules.TryCreateReserve(75f, out float second);

            Assert.That(first, Is.EqualTo(second));
        }

        [TestCase(0f)]
        [TestCase(-5f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void TryCreateReserve_InvalidConfigurationIsRejected(float configured)
        {
            bool created = DownedHealthRules.TryCreateReserve(configured, out float reserve);

            Assert.That(created, Is.False);
            Assert.That(reserve, Is.EqualTo(0f));
        }

        [Test]
        public void Drain_ReducesByRateTimesDelta()
        {
            Assert.That(DownedHealthRules.Drain(75f, 2.5f, 2f), Is.EqualTo(70f).Within(0.0001f));
        }

        [Test]
        public void Drain_ClampsAtZero()
        {
            Assert.That(DownedHealthRules.Drain(1f, 2.5f, 2f), Is.EqualTo(0f));
        }

        [TestCase(-1f, 1f)]
        [TestCase(float.NaN, 1f)]
        [TestCase(1f, -1f)]
        [TestCase(1f, float.NaN)]
        [TestCase(float.PositiveInfinity, 1f)]
        public void Drain_InvalidInputsLeaveReserveUnchanged(float rate, float deltaTime)
        {
            Assert.That(DownedHealthRules.Drain(10f, rate, deltaTime), Is.EqualTo(10f));
        }

        [Test]
        public void ApplyDamage_AppliesMultiplier()
        {
            Assert.That(DownedHealthRules.ApplyDamage(75f, 10f, 1.5f), Is.EqualTo(60f).Within(0.0001f));
        }

        [Test]
        public void ApplyDamage_ClampsAtZeroAndDiscardsExcess()
        {
            Assert.That(DownedHealthRules.ApplyDamage(5f, 100f, 1f), Is.EqualTo(0f));
        }

        [TestCase(-1f, 1f)]
        [TestCase(float.NaN, 1f)]
        [TestCase(10f, -1f)]
        [TestCase(10f, float.NaN)]
        public void ApplyDamage_InvalidInputsLeaveReserveUnchanged(float damage, float multiplier)
        {
            Assert.That(DownedHealthRules.ApplyDamage(20f, damage, multiplier), Is.EqualTo(20f));
        }

        [Test]
        public void ApplyDamage_InvalidCurrentIsTreatedAsDepleted()
        {
            Assert.That(DownedHealthRules.ApplyDamage(float.NaN, 1f, 1f), Is.EqualTo(0f));
        }

        [Test]
        public void AcceleratedDrainRate_EmptiesRemainingReserveInConfiguredSeconds()
        {
            Assert.That(DownedHealthRules.AcceleratedDrainRate(2.5f, 75f, 5f), Is.EqualTo(15f).Within(0.0001f));
        }

        [Test]
        public void AcceleratedDrainRate_NeverDrainsSlowerThanTheNormalRate()
        {
            Assert.That(DownedHealthRules.AcceleratedDrainRate(2.5f, 5f, 5f), Is.EqualTo(2.5f));
        }

        [TestCase(2.5f, 75f, 0f)]
        [TestCase(2.5f, 75f, -1f)]
        [TestCase(2.5f, 75f, float.NaN)]
        [TestCase(2.5f, 75f, float.PositiveInfinity)]
        [TestCase(2.5f, float.NaN, 5f)]
        [TestCase(2.5f, -3f, 5f)]
        public void AcceleratedDrainRate_InvalidInputsKeepTheNormalRate(float baseRate, float reserve, float seconds)
        {
            Assert.That(DownedHealthRules.AcceleratedDrainRate(baseRate, reserve, seconds), Is.EqualTo(baseRate));
        }

        [Test]
        public void AcceleratedDrainRate_DepletesAboutFiveSecondsAfterActivationWithoutCuttingTheBar()
        {
            const float tick = 1f / 60f;
            float rate = DownedHealthRules.AcceleratedDrainRate(2.5f, 60f, 5f);
            float reserve = DownedHealthRules.Drain(60f, rate, tick);
            Assert.That(reserve, Is.EqualTo(60f - rate * tick).Within(0.0001f), "First tick only drains one step.");

            int ticks = 1;
            while (!DownedHealthRules.IsDepleted(reserve) && ticks < 1000)
            {
                reserve = DownedHealthRules.Drain(reserve, rate, tick);
                ticks++;
            }

            Assert.That(ticks * tick, Is.EqualTo(5f).Within(2f * tick));
        }

        [Test]
        public void IsDepleted_TrueOnlyAtOrBelowZeroOrInvalid()
        {
            Assert.That(DownedHealthRules.IsDepleted(0f), Is.True);
            Assert.That(DownedHealthRules.IsDepleted(float.NaN), Is.True);
            Assert.That(DownedHealthRules.IsDepleted(0.01f), Is.False);
        }
    }
}
