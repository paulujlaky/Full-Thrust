// Linear remapping shared by noise generators and volumes.

float Remap(float value, float low, float high, float newLow, float newHigh) {

    return newLow + (value - low) / max(high - low, 1e-5) * (newHigh - newLow);

}
