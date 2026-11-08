import SwiftUI

struct SlotBarView: View {
    @ObservedObject var viewModel: GameViewModel

    var body: some View {
        HStack(spacing: 12) {
            ForEach(0..<Rules.slotCount, id: \.self) { slot in
                VStack(spacing: 2) {
                    ZStack {
                        RoundedRectangle(cornerRadius: 16)
                            .fill(Color.white.opacity(0.68))
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
