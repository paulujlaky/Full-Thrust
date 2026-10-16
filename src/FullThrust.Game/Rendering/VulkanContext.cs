using Brutal.GlfwApi;
using Brutal.Logging;
using Brutal.Pointers.Extensions;
using Brutal.Strings;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace FullThrust.Game.Rendering;

public sealed class VulkanContext : IDisposable {

    private static readonly LogCategory Log = LogCategory.Make("Render");

    private readonly VkDebugUtilsMessengerEXT _messenger;

    public Instance Instance { get; }

    public VkSurfaceKHR Surface { get; }

    public PhysicalDevice PhysicalDevice { get; }

    public DeviceEx Device { get; }

    public Queue GraphicsQueue { get; }

    public int GraphicsFamily { get; }

    public string DeviceName { get; }

    public bool ValidationEnabled { get; }

    public unsafe VulkanContext(GlfwWindow window, bool enableValidation) {

        var extensions = new HashSet<string>();
        var layers = new HashSet<string>();

        var required = Glfw.GetRequiredInstanceExtensions(out var count);

        for (var i = 0; i < count; i++) {

            extensions.Add(Utf8.GetString(required[i]));

        }

        VulkanHelpers.AddPlatformRequiredExtensions(extensions);

        ValidationEnabled = enableValidation && VulkanHelpers.Validation.IsSupported;

        if (ValidationEnabled) {

            VulkanHelpers.AddValidationLayers(layers);
            extensions.Add(VK.EXT_DEBUG_UTILS_EXTENSION_NAME);

        } else if (enableValidation) {

            Log.Warning("Vulkan validation layers requested but not installed; install the Vulkan SDK to enable them.");

        }

        Instance = Vulkan.CreateInstance(new Instance.CreateInfo {

            AppInfo = new VkApplicationInfo {

                ApplicationName = "Full Thrust"u8.AsPointer(),
                EngineName = "BRUTAL"u8.AsPointer(),
                ApiVersion = (int)VulkanHelpers.Api.VERSION_1_3,

            },

            Flags = VulkanHelpers.GetPlatformRequiredInstanceCreateFlags(),
            EnabledExtensions = extensions,
            EnabledLayers = layers,

        });

        if (ValidationEnabled) {

            var messengerInfo = VulkanHelpers.BuildDefaultDebugMessengerCreateInfo(
                new PfnVkDebugUtilsMessengerCallbackEXT(&VulkanHelpers.DebugUtilsMessengerCallbacks.Default));

            _messenger = Instance.CreateDebugUtilsMessengerEXT(messengerInfo, null);

        }

        if (Glfw.CreateWindowSurface(Instance.VkHandle, window, out var surface) != 0) {

            throw new InvalidOperationException("Failed to create a Vulkan window surface.");

        }

        Surface = new VkSurfaceKHR((nint)surface);

        (PhysicalDevice, GraphicsFamily) = PickPhysicalDevice();
        DeviceName = PhysicalDevice.GetDeviceName();

        Log.Info($"Using GPU {DeviceName}");

        var priority = 1.0f;

        var queueInfo = new VkDeviceQueueCreateInfo {

            QueueFamilyIndex = GraphicsFamily,
            QueueCount = 1,
            QueuePriorities = &priority,

        };

        var features = new VkPhysicalDeviceFeatures();
        var features13 = new VkPhysicalDeviceVulkan13Features { Synchronization2 = true };

        var deviceExtensions = new HashSet<string>();

        VulkanHelpers.AddSwapchainExtensions(deviceExtensions);
        VulkanHelpers.AddMandatoryIfSupportedDeviceExtensions(deviceExtensions, PhysicalDevice);

        Device = PhysicalDevice.CreateDevice(new DeviceEx.CreateInfo {

            Extensions = deviceExtensions,
            Features = &features,
            QueueCreateInfos = new ReadOnlySpan<VkDeviceQueueCreateInfo>(&queueInfo, 1),
            Next = &features13,

        });

        GraphicsQueue = Device.GetQueue(GraphicsFamily, 0);

    }

    private (PhysicalDevice Device, int Family) PickPhysicalDevice() {

        PhysicalDevice? best = null;

        var bestFamily = -1;
        var bestScore = int.MinValue;

        foreach (var candidate in Instance.PhysicalDevices) {

            var properties = candidate.GetProperties();

            if (properties.ApiVersion < (int)VulkanHelpers.Api.VERSION_1_3) {

                continue;

            }

            if (!candidate.SupportsExtension(VK.KHR_SWAPCHAIN_EXTENSION_NAME)) {

                continue;

            }

            var family = FindPresentableGraphicsFamily(candidate);

            if (family < 0) {

                continue;

            }

            var score = properties.DeviceType switch {

                VkPhysicalDeviceType.DiscreteGpu => 1000,
                VkPhysicalDeviceType.IntegratedGpu => 100,
                _ => 0,

            };

            if (score > bestScore) {

                best = candidate;
                bestFamily = family;
                bestScore = score;

            }

        }

        if (best is null) {

            throw new InvalidOperationException("No Vulkan 1.3 GPU with graphics and present support was found.");

        }

        return (best, bestFamily);

    }

    private int FindPresentableGraphicsFamily(PhysicalDevice candidate) {

        var count = candidate.GetQueueFamilyProperties_Count();

        Span<VkQueueFamilyProperties> families = stackalloc VkQueueFamilyProperties[count];

        candidate.GetQueueFamilyProperties_Write(families);

        for (var i = 0; i < count; i++) {

            if (families[i].QueueFlags.HasFlag(VkQueueFlags.GraphicsBit) && candidate.GetSurfaceSupportKHR(i, Surface)) {

                return i;

            }

        }

        return -1;

    }

    public void Dispose() {

        Device.WaitIdle();
        Device.Dispose();

        Instance.DestroySurfaceKHR(Surface, null);

        if (!_messenger.IsNull()) {

            Instance.DestroyDebugUtilsMessengerEXT(_messenger, null);

        }

        Instance.Dispose();

    }

}
