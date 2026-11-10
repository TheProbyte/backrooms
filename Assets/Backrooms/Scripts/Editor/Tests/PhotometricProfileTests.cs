using Backrooms.Generation;
using NUnit.Framework;
using UnityEngine;

namespace Backrooms.Tests
{
    /// <summary>The fixtures' measured light shapes (PhotometricProfiles: IES LM-63 files fitted as cos^k).</summary>
    public class PhotometricProfileTests
    {
        [Test]
        public void ALambertianFileFitsExponentOne()
        {
            // cos(angle) x 1000 cd: a perfect diffuser.
            string ies = "IESNA:LM-63-2002\nTILT=NONE\n1 1000 1.0 10 1 1 2 0.6 0.6 0\n1 1 40\n0 10 20 30 40 50 60 70 80 90\n0\n"
                       + "1000 984.8 939.7 866 766 642.8 500 342 173.6 0\n";
            Assert.IsTrue(PhotometricProfiles.TryParse(ies, out var a, out var c));
            Assert.AreEqual(10, a.Length);
            Assert.AreEqual(1f, PhotometricProfiles.Fit(a, c), 0.03f);
        }

        [Test]
        public void HorizontalPlanesAreAveragedAndTheMultiplierApplied()
        {
            string ies = "IESNA:LM-63-2002\nTILT=NONE\n1 1000 2.0 3 2 1 2 0.6 0.6 0\n1 1 40\n0 45 90\n0 90\n100 60 0\n100 80 0\n";
            Assert.IsTrue(PhotometricProfiles.TryParse(ies, out var a, out var c));
            Assert.AreEqual(200f, c[0], 1e-3f);
            Assert.AreEqual(140f, c[1], 1e-3f);
        }

        [Test]
        public void TheBundledFixturesHaveSensibleShapes()
        {
            // A prismatic troffer is about Lambertian, a little narrower; a dome and bare tubes throw wider.
            float troffer = PhotometricProfiles.Exponent(false, false), dome = PhotometricProfiles.Exponent(true, false), strip = PhotometricProfiles.Exponent(false, true);
            Assert.That(troffer, Is.InRange(0.9f, 1.4f));
            Assert.Less(dome, troffer);
            Assert.Less(strip, troffer);
            Assert.Greater(strip, 0.1f);
        }
    }
}
