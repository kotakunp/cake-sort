import SwiftUI

struct HUDView: View {
    @ObservedObject var viewModel: GameViewModel

    var body: some View {
        VStack(spacing: 7) {
            HStack {
                VStack(alignment: .leading, spacing: 1) {
                    Text("Level \(viewModel.level + 1)")
                        .font(.title2.weight(.heavy))
                    Text("\(viewModel.cakesServed) cakes served")
                        .font(.caption)
                        .foregroundColor(FlavorPalette.ink.opacity(0.7))
                }
                Spacer()
                VStack(alignment: .trailing, spacing: 1) {
                    Text("\(viewModel.score) / \(viewModel.targetScore)")
                        .font(.headline.monospacedDigit())
                    Text("Best \(viewModel.highScore)")
                        .font(.caption)
                        .foregroundColor(FlavorPalette.ink.opacity(0.7))
                }
                Button(action: viewModel.toggleSound) {
                    Image(systemName: viewModel.soundEnabled ? "speaker.wave.2.fill" : "speaker.slash.fill")
                        .font(.title3)
                        .frame(width: 38, height: 38)
                        .background(Circle().fill(Color.white.opacity(0.72)))
                }
                .accessibilityLabel(Text(viewModel.soundEnabled ? "Mute sounds" : "Enable sounds"))
            }
            GeometryReader { proxy in
                let progress = min(1, Double(viewModel.score) / Double(max(1, viewModel.targetScore)))
                ZStack(alignment: .leading) {
                    Capsule()
                        .fill(Color.white.opacity(0.65))
                        .shadow(color: Color(hex: 0x6D4C41).opacity(0.18), radius: 2, x: 0, y: 1)
                    Capsule()
                        .fill(LinearGradient(colors: [FlavorPalette.accentLight, FlavorPalette.accent],
                                             startPoint: .leading, endPoint: .trailing))
                        .frame(width: progress > 0 ? max(10, proxy.size.width * progress) : 0)
                        .animation(.spring(response: 0.4, dampingFraction: 0.8), value: viewModel.score)
                }
            }
            .frame(height: 10)
        }
        .foregroundColor(FlavorPalette.ink)
        .padding(.horizontal, 16)
        .padding(.top, 4)
    }
}

struct BoosterBarView: View {
    @ObservedObject var viewModel: GameViewModel

    var body: some View {
        HStack(spacing: 12) {
            BoosterButton(title: "Hammer", systemImage: "hammer.fill",
                          count: viewModel.hammerCount,
                          isSelected: viewModel.hammerArmed,
                          isDisabled: viewModel.isBusy || viewModel.hammerCount == 0,
                          action: viewModel.armHammer)
            BoosterButton(title: "Shuffle", systemImage: "shuffle",
                          count: viewModel.shuffleCount,
                          isSelected: false,
                          isDisabled: viewModel.isBusy || viewModel.shuffleCount == 0,
                          action: viewModel.shuffle)
        }
        .padding(.horizontal, 16)
    }
}

private struct BoosterButton: View {
    let title: String
    let systemImage: String
    let count: Int
    let isSelected: Bool
    let isDisabled: Bool
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            HStack {
                Image(systemName: systemImage)
                Text(title)
                    .font(.subheadline.weight(.bold))
                Spacer()
                Text("\(count)")
                    .font(.caption.weight(.heavy))
                    .frame(width: 24, height: 24)
                    .background(Circle().fill(Color.white.opacity(0.75)))
            }
            .foregroundColor(FlavorPalette.ink)
            .padding(.horizontal, 12)
            .padding(.vertical, 8)
            .frame(maxWidth: .infinity)
            .background(
                RoundedRectangle(cornerRadius: 13)
                    .fill(LinearGradient(
                        colors: isSelected
                            ? [Color(hex: 0xFFE0B2), Color(hex: 0xFFCC80)]
                            : [Color.white, Color(hex: 0xFFF1DC)],
                        startPoint: .top, endPoint: .bottom))
                    .shadow(color: Color(hex: 0x6D4C41).opacity(0.22), radius: 4, x: 0, y: 2)
            )
            .overlay(
                RoundedRectangle(cornerRadius: 13)
                    .stroke(isSelected ? FlavorPalette.accent : Color.clear, lineWidth: 2)
            )
        }
        .disabled(isDisabled)
        .opacity(isDisabled ? 0.45 : 1)
    }
}
