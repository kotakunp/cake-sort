import SwiftUI

struct SlotBarView: View {
    @ObservedObject var viewModel: GameViewModel

    var body: some View {
        HStack(spacing: 12) {
            ForEach(0..<Rules.slotCount, id: \.self) { slot in
                VStack(spacing: 2) {
                    ZStack {
                        RoundedRectangle(cornerRadius: 16)
                            .fill(LinearGradient(colors: [Color.white, Color(hex: 0xFFF1DC)],
                                                 startPoint: .top, endPoint: .bottom))
                            .shadow(color: Color(hex: 0x6D4C41).opacity(0.25), radius: 5, x: 0, y: 3)
                        if let cake = viewModel.slots[slot] {
                            CakeView(slices: cake.slices)
                                .padding(7)
                                .transition(.scale.combined(with: .opacity))
                        }
                        RoundedRectangle(cornerRadius: 16)
                            .stroke(viewModel.selectedSlot == slot ? FlavorPalette.accent : Color.clear,
                                    lineWidth: 3)
                    }
                    .frame(height: 88)
                    .scaleEffect(viewModel.selectedSlot == slot ? 1.06 : 1)
                    .offset(x: viewModel.shakingSlot == slot ? -6 : 0)
                    .contentShape(Rectangle())
                    .onTapGesture {
                        viewModel.selectSlot(slot)
                    }
                    .gesture(
                        DragGesture(minimumDistance: 5,
                                    coordinateSpace: .named(GameViewModel.gameCoordinateSpace))
                            .onChanged { value in
                                if viewModel.draggingSlot != slot {
                                    viewModel.beginDrag(slot: slot, at: value.startLocation)
                                }
                                viewModel.updateDrag(to: value.location)
                            }
                            .onEnded { value in
                                viewModel.endDrag(at: value.location)
                            }
                    )
                    Text("Cake \(slot + 1)")
                        .font(.caption.weight(.semibold))
                        .foregroundColor(FlavorPalette.ink.opacity(0.75))
                }
                .frame(maxWidth: .infinity)
            }
        }
    }
}
